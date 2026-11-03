using Retro.Systems.Nes;

namespace Retro.App;

public sealed partial class EmulatorHost
{
    private readonly NesBatteryStore? _batteryStore;
    private NesBatterySession? _battery;
    private string? _unsupportedBattery;
    public EmulatorHost() : this(null) { }
    public EmulatorHost(NesBatteryStore? batteryStore) => _batteryStore = batteryStore;
    public NesBatterySession? Battery => Fusion == null ? _battery : null;
    public string? BatteryProblem => _battery?.Problem ?? _unsupportedBattery;
    public bool BatteryAtRisk => _battery is { Pending: true, HasRecovery: false };

    public void PollBattery(double seconds) => _battery?.Poll(seconds);
    public bool TryPreserveBattery() => _battery == null || _battery.Flush() || _battery.HasRecovery;

    public void EnsureBatteryPreserved()
    {
        if (!TryPreserveBattery())
            throw new IOException(UiText.Format($"No se pudo guardar la batería NES: {UiText.Raw(UiText.Message(BatteryProblem ?? ""))}"));
    }

    public void RestoreBattery(string path)
    {
        if (Battery == null) throw new InvalidOperationException("No hay una batería NES disponible.");
        SessionLeaving?.Invoke();
        Battery.Restore(path);
        SessionChanging?.Invoke();
        StateRestored();
        Paused = true;
        Status = "Batería recuperada. Los datos anteriores se conservaron en respaldos.";
    }

    private void AttachBattery(Retro.Core.IMachine machine)
    {
        _battery = null; _unsupportedBattery = null;
        if (_batteryStore == null || machine is not Nes { Cartridge.HasBattery: true } nes) return;
        if (nes.Cartridge.SupportsBatterySave) _battery = _batteryStore.Open(nes);
        else _unsupportedBattery = "Esta variante de batería NES todavía no tiene guardado automático. Usá las ranuras de estados para conservar la partida.";
    }
}
