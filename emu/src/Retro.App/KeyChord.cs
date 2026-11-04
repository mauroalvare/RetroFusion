using ImGuiNET;
using Raylib_cs;

namespace Retro.App;

/// <summary>
/// A configurable emulator shortcut: one main key with optional Ctrl and Shift, stored as text
/// ("F1", "Ctrl+Shift+S", "Pause"). Only chords that neither type into the game nor repeat another
/// host action are accepted. F2–F12 stay reserved even with modifiers because their handlers ignore them.
/// </summary>
public readonly record struct KeyChord(string Key, bool Ctrl = false, bool Shift = false)
{
    /// <summary>F1 is the only function key the emulator leaves free, and it needs no modifier on most machines.</summary>
    public static readonly KeyChord CaptureDefault = new("F1");

    public override string ToString() => (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + Key;
    /// <summary>The chord as menus and help show it, in the interface language.</summary>
    public string Label => (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Key == "Pause" ? UiText.Get("Pausa") : Key);

    private static readonly (string Name, ImGuiKey Gui, KeyboardKey Raylib)[] Keys = BuildKeys();
    private static (string, ImGuiKey, KeyboardKey)[] BuildKeys()
    {
        var keys = new List<(string, ImGuiKey, KeyboardKey)>();
        for (int i = 1; i <= 12; i++) keys.Add(("F" + i, ImGuiKey.F1 + (i - 1), KeyboardKey.F1 + (i - 1)));
        for (char c = 'A'; c <= 'Z'; c++) keys.Add((c.ToString(), ImGuiKey.A + (c - 'A'), KeyboardKey.A + (c - 'A')));
        for (int i = 0; i <= 9; i++) keys.Add((i.ToString(), ImGuiKey._0 + i, KeyboardKey.Zero + i));
        keys.Add(("Pause", ImGuiKey.Pause, KeyboardKey.Pause));
        keys.Add(("PrintScreen", ImGuiKey.PrintScreen, KeyboardKey.PrintScreen));
        return keys.ToArray();
    }

    private static int IndexOf(string name) => Array.FindIndex(Keys, key => key.Name == name);
    private bool Known => IndexOf(Key) >= 0;

    /// <summary>Reads typed text such as "ctrl + shift + s", "F1" or "Pausa"; null when it is a valid, free chord.</summary>
    public static string? TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        string[] parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        // "Ctrl+" or "Ctrl+Shift" still lack the main key.
        if (parts.Length == 0 || (text ?? "").TrimEnd().EndsWith('+') || IsModifier(parts[^1])) return UiText.Get("Escribí una tecla, por ejemplo F1 o Ctrl+Shift+S.");
        bool ctrl = false, shift = false;
        foreach (string modifier in parts[..^1])
            switch (CommandLine.Fold(modifier).Replace(" ", ""))
            {
                case "ctrl" or "control" or "ctl": ctrl = true; break;
                case "shift" or "mayus" or "mayusculas": shift = true; break;
                case "alt" or "altgr" or "option": return AltProblem;
                default: return UiText.Format($"No conozco el modificador {UiText.Raw(modifier)}. Usá Ctrl o Shift.");
            }
        if (Canonical(parts[^1]) is not { } key) return UnsupportedProblem;
        chord = new(key, ctrl, shift);
        return chord.Problem();
    }

    private static bool IsModifier(string token) => CommandLine.Fold(token).Replace(" ", "") is "ctrl" or "control" or "ctl" or "shift" or "mayus" or "mayusculas";

    private static string? Canonical(string token)
    {
        string folded = CommandLine.Fold(token).Replace(" ", "");
        if (folded is "pause" or "pausa" or "inter" or "break") return "Pause";
        if (folded is "printscreen" or "imprpant" or "impr" or "print" or "prtsc" or "imprimirpantalla") return "PrintScreen";
        string upper = folded.ToUpperInvariant();
        return Keys.Any(key => key.Name == upper) ? upper : null;
    }

    private static string AltProblem => UiText.Get("Alt abre los menús y en varios juegos es un botón. Combiná la tecla con Ctrl o Shift.");
    private static string UnsupportedProblem => UiText.Get("Esa tecla maneja el juego o la interfaz. Probá F1, Pausa o Ctrl con una letra o un número.");

    /// <summary>Why this chord cannot save captures, or null when it is free on every machine.</summary>
    public string? Problem()
    {
        if (!Known) return UnsupportedProblem;
        if (Key == "PrintScreen") return UiText.Get("Impr Pant suele abrir la herramienta de capturas del sistema operativo. Elegí otra tecla.");
        if (Key == "Pause") return Ctrl || Shift ? UiText.Get("Usá Pausa sola: con Ctrl el sistema la convierte en Inter.") : null;
        if (Key[0] == 'F' && Key.Length > 1)
        {
            int number = int.Parse(Key[1..], System.Globalization.CultureInfo.InvariantCulture);
            return number == 1 ? null : UiText.Format($"{Key} ya se usa para {UiText.Get(HostFunction(number))}, también con Ctrl o Shift.");
        }
        if (!Ctrl) return UiText.Get("Sin Ctrl, esa tecla llega al juego y se activaría al escribir. Combinala con Ctrl.");
        return ReservedWithCtrl(Key[0]) is { } action ? UiText.Format($"Ctrl+{Key} ya se usa para {UiText.Get(action)}.") : null;
    }

    private static string HostFunction(int number) => number switch
    {
        2 => "guardar el estado rápido",
        3 or 4 => "cargar el estado rápido",
        5 => "reiniciar la partida",
        6 => "reproducir o detener la cinta",
        7 => "rebobinar la cinta",
        8 => "el turbo",
        9 => "pausar y continuar",
        10 => "avanzar paso a paso",
        11 => "la pantalla completa",
        _ => "abrir el depurador"
    };

    private static string? ReservedWithCtrl(char key) => key switch
    {
        'O' => "abrir un juego",
        'L' => "ir a la biblioteca",
        'M' => "modificar el juego",
        'K' => "ver el código",
        'P' => "buscar acciones",
        'T' => "alternar la consola",
        'F' => "buscar en la biblioteca",
        'A' or 'C' or 'V' or 'X' or 'Z' or 'Y' => "editar texto",
        _ => null
    };

    /// <summary>
    /// The chord in effect for the running machine. Where F1 belongs to the game (PC and C64 originals)
    /// it takes Ctrl, and where Ctrl is a machine key (Spectrum, C64, PC) a letter takes Shift, the same
    /// rules as the other emulator keys, so the menus can show exactly what works.
    /// </summary>
    public KeyChord For(bool machineOwnsF1, bool ctrlIsMachineKey)
    {
        if (Key[0] == 'F' && Key.Length > 1 && machineOwnsF1 && !Ctrl) return this with { Ctrl = true };
        if (Key.Length == 1 && ctrlIsMachineKey && !Shift) return this with { Shift = true };
        return this;
    }

    /// <summary>The chord was pressed during the last ImGui frame: exact modifiers, never a key repeat.</summary>
    public bool Pressed() => Pressed(ImGui.GetIO().KeyCtrl, ImGui.GetIO().KeyShift, ImGui.GetIO().KeyAlt || ImGui.GetIO().KeySuper,
        key => ImGui.IsKeyPressed(key, false));

    internal bool Pressed(bool ctrl, bool shift, bool other, Func<ImGuiKey, bool> pressed) =>
        Known && ctrl == Ctrl && shift == Shift && !other && pressed(Keys[IndexOf(Key)].Gui);

    /// <summary>The whole chord is down on the host keyboard, so none of its keys may reach the machine.</summary>
    public bool Held(Func<KeyboardKey, bool> down) => Known && down(Keys[IndexOf(Key)].Raylib)
        && (down(KeyboardKey.LeftControl) || down(KeyboardKey.RightControl)) == Ctrl
        && (down(KeyboardKey.LeftShift) || down(KeyboardKey.RightShift)) == Shift;

    /// <summary>
    /// What the user pressed while a shortcut is being chosen: the chord (maybe invalid), Escape to cancel,
    /// or nothing. Modifiers alone wait for the main key.
    /// </summary>
    public static (KeyChord? Chord, string? Problem, bool Cancel) Listen()
    {
        var io = ImGui.GetIO();
        return Listen(io.KeyCtrl, io.KeyShift, io.KeyAlt || io.KeySuper, key => ImGui.IsKeyPressed(key, false));
    }

    internal static (KeyChord? Chord, string? Problem, bool Cancel) Listen(bool ctrl, bool shift, bool other, Func<ImGuiKey, bool> pressed)
    {
        if (pressed(ImGuiKey.Escape)) return (null, null, true);
        for (var key = ImGuiKey.NamedKey_BEGIN; key <= ImGuiKey.KeypadEqual; key++)
        {
            if (key is >= ImGuiKey.LeftCtrl and <= ImGuiKey.RightSuper || !pressed(key)) continue;
            if (other) return (null, AltProblem, false);
            string? name = Keys.FirstOrDefault(entry => entry.Gui == key).Name;
            if (name == null) return (null, UnsupportedProblem, false);
            var chord = new KeyChord(name, ctrl, shift);
            return (chord, chord.Problem(), false);
        }
        return (null, null, false);
    }
}
