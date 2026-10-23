namespace Retro.Systems.Pc;

/// <summary>
/// Functional 8253: three independent binary counters, modes 0, 2 and 3.
/// Tick supplies a complete external CLK pulse; it is not a CPU instruction or cycle.
/// Gate and I/O changes occur between pulses; electrical setup/hold timing is not modeled.
/// </summary>
public sealed class Pit8253
{
    private sealed class Channel
    {
        public int Access, Mode;
        public ushort Reload, Count, Latch;
        public byte Low;
        public bool Configured, HasReload, Loaded, LoadPending, Counting;
        public bool Gate, Output, WriteHigh, ReadHigh, Latched;
    }

    private readonly Channel[] _channels = [new(), new(), new()];

    /// <summary>Synchronous level changes, including programming and GATE. Callers must not reenter the PIT.</summary>
    public event Action<int, bool>? OutputChanged;

    public bool GetOutput(int channel) => Get(channel).Output;

    /// <summary>Deterministic board reset, not a reset pin on the physical 8253. All channels need programming.</summary>
    public void Reset()
    {
        for (int channel = 0; channel < 3; channel++)
        {
            bool wasHigh = _channels[channel].Output;
            _channels[channel] = new Channel();
            if (wasHigh) OutputChanged?.Invoke(channel, false);
        }
    }

    public void WriteControl(byte value)
    {
        int channel = value >> 6, access = (value >> 4) & 3, mode = (value >> 1) & 7;
        if (channel == 3)
            throw new NotSupportedException("8253: selección de contador inválida; read-back pertenece al 8254.");
        Channel current = _channels[channel];
        if (access == 0)
        {
            RequireReadable(current);
            if (current.ReadHigh || current.Latched)
                throw new InvalidOperationException("8253: completá la lectura anterior antes de pedir otro latch.");
            current.Latch = current.Count; current.Latched = true;
            return; // The mode and BCD bits are don't-care for a latch command.
        }
        if ((mode & 2) != 0) mode &= 3; // Modes 6/7 alias 2/3.
        if ((value & 1) != 0 || mode is not (0 or 2 or 3))
            throw new NotSupportedException("8253: por ahora se admiten los modos binarios 0, 2 y 3.");
        // A new mode discards incomplete transfers; unsupported commands leave everything intact.
        var next = new Channel { Configured = true, Access = access, Mode = mode,
            Gate = current.Gate, Output = current.Output };
        _channels[channel] = next;
        SetOutput(channel, next, mode != 0);
    }

    public void WriteCounter(int channel, byte value)
    {
        Channel counter = Get(channel);
        RequireConfigured(counter);
        // Unlike the 8254, the 8253 requires a complete word read before loading the same counter.
        if (counter.ReadHigh || counter.Latched)
            throw new InvalidOperationException("8253: completá la lectura del contador antes de escribir.");
        if (counter.Access == 3 && !counter.WriteHigh)
        {
            counter.Low = value; counter.WriteHigh = true;
            if (counter.Mode == 0)
            {
                counter.Counting = counter.LoadPending = false;
                SetOutput(channel, counter, false);
            }
            return;
        }
        ushort reload = counter.Access switch
        {
            1 => value, 2 => (ushort)(value << 8), _ => (ushort)(counter.Low | (value << 8))
        };
        if ((counter.Mode == 2 && reload == 1) || (counter.Mode == 3 && reload is 1 or 3))
            throw new NotSupportedException("8253: divisor 1 y divisor 3 en modo 3 pendientes de contraste de silicio.");
        counter.WriteHigh = false;
        counter.Reload = reload; counter.HasReload = true;
        if (counter.Mode == 0 || !counter.Loaded) counter.LoadPending = true;
        if (counter.Mode == 0) SetOutput(channel, counter, false);
    }

    public byte ReadCounter(int channel)
    {
        Channel counter = Get(channel);
        RequireReadable(counter);
        ushort value = counter.Latched ? counter.Latch : counter.Count;
        byte result = counter.Access == 2 || counter.ReadHigh ? (byte)(value >> 8) : (byte)value;
        if (counter.Access == 3) counter.ReadHigh = !counter.ReadHigh;
        if (!counter.ReadHigh) counter.Latched = false;
        return result;
    }

    public void SetGate(int channel, bool high)
    {
        Channel counter = Get(channel);
        bool rising = !counter.Gate && high;
        counter.Gate = high;
        if (!counter.Configured || counter.Mode == 0) return;
        if (!high) SetOutput(channel, counter, true);
        if (rising && counter.HasReload) counter.LoadPending = true;
    }

    /// <summary>One rising/falling CLK pair on one channel. Loading consumes a pulse without decrementing.</summary>
    public void Tick(int channel)
    {
        Channel counter = Get(channel);
        if (!counter.Configured || !counter.HasReload) return;
        if (counter.LoadPending)
        {
            counter.Count = counter.Reload;
            counter.LoadPending = false; counter.Loaded = counter.Counting = true;
            if (counter.Mode != 0) SetOutput(channel, counter, true);
            return;
        }
        if (!counter.Gate || !counter.Counting) return;
        switch (counter.Mode)
        {
            case 0:
                counter.Count = unchecked((ushort)(counter.Count - 1));
                if (counter.Count == 0) SetOutput(channel, counter, true);
                break;
            case 2:
                if (counter.Count == 1)
                {
                    counter.Count = counter.Reload;
                    SetOutput(channel, counter, true);
                }
                else
                {
                    counter.Count = unchecked((ushort)(counter.Count - 1));
                    if (counter.Count == 1) SetOutput(channel, counter, false);
                }
                break;
            case 3:
                // Original 8253 odd count sequence; the 8254 uses a different counting element.
                int decrement = (counter.Count & 1) == 0 ? 2 : counter.Output ? 1 : 3;
                counter.Count = unchecked((ushort)(counter.Count - decrement));
                if (counter.Count == 0)
                {
                    counter.Count = counter.Reload;
                    SetOutput(channel, counter, !counter.Output);
                }
                break;
        }
    }

    private void SetOutput(int channel, Channel counter, bool high)
    {
        if (counter.Output == high) return;
        counter.Output = high;
        OutputChanged?.Invoke(channel, high);
    }

    private Channel Get(int channel) => (uint)channel < 3 ? _channels[channel]
        : throw new ArgumentOutOfRangeException(nameof(channel));

    private static void RequireConfigured(Channel counter)
    {
        if (!counter.Configured)
            throw new InvalidOperationException("8253: falta programar el modo del contador.");
    }

    private static void RequireReadable(Channel counter)
    {
        RequireConfigured(counter);
        if (!counter.Loaded || counter.WriteHigh)
            throw new InvalidOperationException("8253: falta completar la carga y darle un pulso de reloj.");
    }
}
