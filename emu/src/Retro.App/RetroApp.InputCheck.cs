using ImGuiNET;
using Raylib_cs;

namespace Retro.App;

public sealed partial class RetroApp
{
    private string _lastInputCheck = "Todavía no se registraron entradas en esta comprobación.";
    private bool _inputCheckOpen;
    private int _inputCheckAutotestFrames = 8;
    private void DrawInputCheck()
    {
        string label = "Comprobar teclado y mando";
        float width = ImGui.GetContentRegionAvail().X - ImGui.GetFontSize() * 2.5f;
        if (ImGui.CalcTextSize(UiText.Get(label)).X > width) label = "Teclado y mando";
        if (ImGui.CalcTextSize(UiText.Get(label)).X > width) label = "Entradas";
        if (!ImGui.CollapsingHeader(UiText.Label(label + UiText.Get("###input-check")), _inputCheckOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None)) return;
        if (_inputCheckOpen && _inputCheckAutotestFrames-- > 0) ImGui.SetScrollHereY(0);
        ImGui.TextWrapped(UiText.Get(Raylib.IsWindowFocused() ? "Ventana con foco. Las entradas se muestran acá mientras probás." : "Hacé clic en esta ventana para darle foco."));
        var down = new List<string>();
        foreach (var key in new[] { KeyboardKey.Left, KeyboardKey.Right, KeyboardKey.Up, KeyboardKey.Down,
            KeyboardKey.Space, KeyboardKey.Z, KeyboardKey.X, KeyboardKey.Enter, KeyboardKey.LeftControl, KeyboardKey.RightControl })
            if (Raylib.IsKeyDown(key)) down.Add(key.ToString());
        if (down.Count > 0) _lastInputCheck = UiText.Get("Última entrada: ") + string.Join(", ", down);
        ImGui.TextWrapped(UiText.Get(down.Count > 0 ? UiText.Get("Ahora: ") + string.Join(", ", down) : "Ahora: las teclas de prueba están sueltas."));
        UiTheme.Hint(_lastInputCheck);
        if (Raylib.IsGamepadAvailable(0))
        {
            ImGui.TextWrapped(UiText.Get(UiText.Get("Mando detectado: ") + Raylib.GetGamepadName_(0)));
            float x = Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftX), y = Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftY);
            using (UiTheme.FixedWidth()) ImGui.TextUnformatted(UiText.Get(UiText.Format($"Palanca X {x:F2} · Y {y:F2}")));
            var buttons = Enum.GetValues<GamepadButton>().Where(b => b != GamepadButton.Unknown && Raylib.IsGamepadButtonDown(0, b)).Select(b => b.ToString()).ToArray();
            ImGui.TextWrapped(UiText.Get(buttons.Length > 0 ? UiText.Get("Botones: ") + string.Join(", ", buttons) : "Soltá y pulsá cada botón; su nombre aparece mientras lo mantenés."));
        }
        else ImGui.TextWrapped(UiText.Get("No hay un mando disponible en el primer puerto. Conectalo y volvé a comprobar acá."));
        UiTheme.Hint("1. Probá flechas, Z, X, Espacio y Enter. 2. Mové la palanca y pulsá los botones. 3. Volvé a Jugar y hacé clic en la pantalla. 4. Si el mando responde acá pero no en el original, revisá Juego > Controles y hardware y el puerto de joystick.");
        UiTheme.Hint("Esta vista comprueba recepción de entradas. El movimiento y las reglas de cada original se contrastan por separado.");
    }
}
