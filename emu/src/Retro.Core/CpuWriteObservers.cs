using System.Runtime.CompilerServices;

namespace Retro.Core;

/// <summary>Independent subscriptions on the emulation thread, preserving an existing write watch.</summary>
public static class CpuWriteObservers
{
    private static readonly ConditionalWeakTable<IWriteWatch, Hub> Hubs = new();

    public static IDisposable Subscribe(IWriteWatch machine, Action<ushort, byte, ushort> observer)
    {
        if (!Hubs.TryGetValue(machine, out var hub) || !ReferenceEquals(machine.WriteWatch, hub.Dispatch))
        {
            Hubs.Remove(machine);
            hub = new Hub(machine);
            Hubs.Add(machine, hub);
        }
        return hub.Add(observer);
    }

    private sealed class Hub
    {
        private readonly IWriteWatch _machine;
        private readonly Action<ushort, byte, ushort>? _previous;
        private Subscription[] _subscriptions = [];
        public Action<ushort, byte, ushort> Dispatch { get; }

        public Hub(IWriteWatch machine)
        {
            _machine = machine; _previous = machine.WriteWatch;
            Dispatch = Notify;
            machine.WriteWatch = Dispatch;
        }

        public IDisposable Add(Action<ushort, byte, ushort> observer)
        {
            var subscription = new Subscription(this, observer);
            _subscriptions = [.. _subscriptions, subscription];
            return subscription;
        }

        private void Notify(ushort address, byte value, ushort pc)
        {
            _previous?.Invoke(address, value, pc);
            foreach (var subscription in _subscriptions)
                if (subscription.Active) subscription.Observer(address, value, pc);
        }

        public void Remove(Subscription subscription)
        {
            _subscriptions = _subscriptions.Where(s => !ReferenceEquals(s, subscription)).ToArray();
            if (_subscriptions.Length != 0) return;
            if (ReferenceEquals(_machine.WriteWatch, Dispatch)) _machine.WriteWatch = _previous;
            if (Hubs.TryGetValue(_machine, out var current) && ReferenceEquals(current, this)) Hubs.Remove(_machine);
        }
    }

    private sealed class Subscription(Hub hub, Action<ushort, byte, ushort> observer) : IDisposable
    {
        public readonly Action<ushort, byte, ushort> Observer = observer;
        public bool Active { get; private set; } = true;
        public void Dispose()
        {
            if (!Active) return;
            Active = false; hub.Remove(this);
        }
    }
}
