using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Retro.Core;

namespace Retro.App;

public sealed partial class EmulatorHost
{
    private readonly RewindBuffer _rewind = new();
    private readonly HashSet<string> _inputKeys = new(StringComparer.Ordinal);
    private readonly List<InputChange> _pendingInputs = [];
    private InputMovie? _recording, _lastRecording, _replay;
    private int _replayFrame;
    private bool _preserveRewind;
    private long _timelineIdentityRevision = -1;
    private string _timelineIdentity = "";
    public bool RewindEnabled { get; private set; }
    public int RewindPoints => _rewind.Count;
    public long RewindBytes => _rewind.Bytes;
    public bool IsRecordingInputs => _recording != null;
    public bool IsReplayingInputs => _replay != null;
    public int RecordedFrames => _recording?.Frames.Count ?? _lastRecording?.Frames.Count ?? 0;
    public bool CanRecordInputs => CanUseStates && !_pcDosBooting && !Debug.Enabled
        && _runToAddress == null && !IsRecordingInputs && !IsReplayingInputs;
    public bool CanReplayInputs => CanRecordInputs && _lastRecording is { Frames.Count: > 0 } movie
        && movie.Identity == TimelineIdentity();
    public event Action? InputReset;

    private string TimelineIdentity()
    {
        if (_timelineIdentityRevision != MediaRevision)
        {
            string source = Machine!.Id + "\0" + FirmwareIdentity + "\0" + string.Join("\0", LoadedMediaHashes.Order(StringComparer.Ordinal));
            _timelineIdentity = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)));
            _timelineIdentityRevision = MediaRevision;
        }
        return _timelineIdentity;
    }
    private static byte[] ScreenHash(IMachine machine) => SHA256.HashData(MemoryMarshal.AsBytes(machine.Video.Pixels.AsSpan()));

    /// <summary>Live keyboard/gamepad transitions, in the order received. Playback owns the input while active.</summary>
    public void SetInput(string key, bool pressed)
    {
        if (Machine is not { } machine || Fusion != null || IsReplayingInputs) return;
        bool changed = pressed ? _inputKeys.Add(key) : _inputKeys.Remove(key);
        if (!changed) return;
        machine.SetKey(key, pressed);
        if (_recording != null)
        {
            if (_pendingInputs.Count >= 1024) { FinishInputRecording(releaseKeys: false); Status = "Grabación detenida: demasiadas entradas sin avanzar un cuadro."; }
            else _pendingInputs.Add(new(key, pressed));
        }
    }

    private void ReleaseTimelineInput()
    {
        Machine?.ReleaseAllKeys(); _inputKeys.Clear(); InputReset?.Invoke();
    }
    private void TimelineStateChanged(bool newMedia = false)
    {
        Machine?.CancelQueuedKeys();
        if (!_preserveRewind) _rewind.Clear();
        _recording = _replay = null; _pendingInputs.Clear(); _replayFrame = 0;
        if (newMedia) _lastRecording = null;
        ReleaseTimelineInput();
    }

    public void SetRewindEnabled(bool enabled)
    {
        RewindEnabled = enabled; _rewind.Clear();
        if (enabled && CanUseStates) CaptureRewind(Machine!);
        if (RewindEnabled) Status = "Rebobinado activado (hasta 64 MB en memoria).";
        else if (!enabled) Status = "Rebobinado desactivado.";
    }
    public void ClearRewind() { _rewind.Clear(); Status = "Historial de rebobinado vaciado."; }

    private void CaptureRewind(IMachine machine)
    {
        if (!RewindEnabled || Fusion != null || IsRecordingInputs || IsReplayingInputs) return;
        long interval = Math.Max(1, (long)Math.Round(machine.FramesPerSecond / 2));
        if (_rewind.LastFrame >= 0 && machine.FrameCount - _rewind.LastFrame < interval) return;
        try
        {
            if (!_rewind.Add(new(machine.FrameCount, machine.SaveState(), machine.Video.Pixels.ToArray())))
            { RewindEnabled = false; Status = "No se pudo conservar el estado dentro del límite de rebobinado."; }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        { RewindEnabled = false; _rewind.Clear(); Status = "No se pudo guardar el rebobinado: " + ex.Message; }
    }

    public bool Rewind(double seconds = 5)
    {
        if (!CanUseStates || IsRecordingInputs || IsReplayingInputs || !double.IsFinite(seconds) || seconds <= 0) return false;
        var machine = Machine!;
        long frames = Math.Max(1, (long)Math.Min(long.MaxValue, Math.Round(Math.Min(seconds, 3600) * machine.FramesPerSecond)));
        var point = _rewind.Find(machine.FrameCount - frames, machine.FrameCount);
        if (point == null) { Status = "Todavía no hay un estado anterior en el historial."; return false; }
        _preserveRewind = true;
        try { RestoreState(point.State, point.Pixels); }
        finally { _preserveRewind = false; }
        _rewind.TrimAfter(point.Frame); Paused = true;
        Status = $"Partida rebobinada al cuadro {point.Frame}. Continuar para jugar desde ahí.";
        return true;
    }

    public bool StartInputRecording(int seconds = 300)
    {
        if (seconds is < 1 or > 300) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!CanRecordInputs) return false;
        var machine = Machine!;
        // Canonicalize the initial state through the same loader used by playback. This releases
        // held keys and cancels queued typing, which is not part of the machine snapshot contract.
        machine.CancelQueuedKeys();
        ReleaseTimelineInput();
        byte[] initial = machine.SaveState(); int[] pixels = machine.Video.Pixels.ToArray();
        RestoreState(initial, pixels);
        _recording = new(TimelineIdentity(), machine.SaveState(), pixels,
            Math.Clamp((int)Math.Ceiling(machine.FramesPerSecond * seconds), 1, 90000));
        _pendingInputs.Clear(); Paused = false;
        Status = "Grabando entradas (hasta cinco minutos). Se verifica la pantalla de cada cuadro.";
        return true;
    }

    public void StopInputRecording()
    {
        FinishInputRecording(releaseKeys: true);
    }
    private void FinishInputRecording(bool releaseKeys)
    {
        if (_recording == null) return;
        _lastRecording = _recording.Frames.Count > 0 ? _recording : null;
        _recording = null; _pendingInputs.Clear();
        if (releaseKeys) ReleaseTimelineInput();
        Status = _lastRecording == null ? "Grabación vacía descartada." : $"Grabación lista: {_lastRecording.Frames.Count} cuadros.";
    }

    public bool StartInputReplay()
    {
        if (!CanReplayInputs) return false;
        var movie = _lastRecording!;
        RestoreState(movie.InitialState, movie.InitialPixels);
        _replay = movie; _replayFrame = 0; Paused = false;
        Status = "Reproduciendo entradas; teclado y mando del juego quedan retenidos hasta terminar.";
        return true;
    }
    public void StopInputReplay()
    {
        if (_replay == null) return;
        _replay = null; _replayFrame = 0; ReleaseTimelineInput(); _audio.Clear(); _accumulator = 0;
        while (Machine?.DrainAudio(_frameAudio) > 0) { }
        Paused = true; Status = "Reproducción detenida.";
    }
    private void StopTimelineForDebug()
    {
        StopInputRecording(); StopInputReplay();
    }
    private void ApplyReplayInputs(IMachine machine)
    {
        if (_replay is not { } movie) return;
        foreach (var change in movie.Frames[_replayFrame].Changes) machine.SetKey(change.Key, change.Pressed);
    }
    private bool CompleteTimelineFrame(IMachine machine)
    {
        if (_recording is { } recording)
        {
            InputChange[] changes = _pendingInputs.ToArray(); _pendingInputs.Clear();
            recording.Frames.Add(new(changes, ScreenHash(machine))); recording.Changes += changes.Length;
            if (recording.Frames.Count >= recording.MaxFrames || recording.Changes >= 100000)
            { StopInputRecording(); Status += " Se alcanzó el límite de grabación."; }
        }
        if (_replay is { } replay)
        {
            if (!CryptographicOperations.FixedTimeEquals(ScreenHash(machine), replay.Frames[_replayFrame].ScreenHash))
            {
                int frame = _replayFrame + 1; StopInputReplay();
                Status = $"La reproducción difiere en el cuadro {frame}; quedó pausada para revisar."; return false;
            }
            if (++_replayFrame == replay.Frames.Count)
            {
                int count = replay.Frames.Count; StopInputReplay();
                Status = $"Reproducción verificada: {count} cuadros coinciden."; return false;
            }
        }
        CaptureRewind(machine); return true;
    }
}
