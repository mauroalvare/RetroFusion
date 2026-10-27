using Raylib_cs;
using Retro.Systems.Spectrum;

namespace Retro.App;

public enum JoystickMode { Kempston, Cursor, Sinclair1 }

/// <summary>
/// Turns PC keyboard and gamepad state into Spectrum key matrix / joystick presses.
/// Only the differences with the previous host frame are sent to the machine.
/// </summary>
public sealed class InputMapper
{
    public JoystickMode Joystick { get; set; } = JoystickMode.Kempston;

    private HashSet<string> _previous = new();

    private static readonly (KeyboardKey key, string[] zx)[] KeyMap = BuildKeyMap();

    private static (KeyboardKey, string[])[] BuildKeyMap()
    {
        var list = new List<(KeyboardKey, string[])>();
        for (char c = 'A'; c <= 'Z'; c++)
            list.Add((Enum.Parse<KeyboardKey>(c.ToString()), new[] { c.ToString() }));
        string[] digits = { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" };
        for (int i = 0; i < 10; i++)
            list.Add((Enum.Parse<KeyboardKey>(digits[i]), new[] { i.ToString() }));
        list.Add((KeyboardKey.Space, new[] { "SPACE" }));
        list.Add((KeyboardKey.Enter, new[] { "ENTER" }));
        list.Add((KeyboardKey.KpEnter, new[] { "ENTER" }));
        list.Add((KeyboardKey.LeftShift, new[] { "CAPS SHIFT" }));
        list.Add((KeyboardKey.RightShift, new[] { "CAPS SHIFT" }));
        list.Add((KeyboardKey.LeftControl, new[] { "SYMBOL SHIFT" }));
        list.Add((KeyboardKey.RightControl, new[] { "SYMBOL SHIFT" }));
        list.Add((KeyboardKey.Backspace, new[] { "CAPS SHIFT", "0" }));
        list.Add((KeyboardKey.Comma, new[] { "SYMBOL SHIFT", "N" }));
        list.Add((KeyboardKey.Period, new[] { "SYMBOL SHIFT", "M" }));
        list.Add((KeyboardKey.Minus, new[] { "SYMBOL SHIFT", "J" }));
        list.Add((KeyboardKey.Equal, new[] { "SYMBOL SHIFT", "L" }));
        list.Add((KeyboardKey.Semicolon, new[] { "SYMBOL SHIFT", "O" }));
        list.Add((KeyboardKey.Slash, new[] { "SYMBOL SHIFT", "V" }));
        list.Add((KeyboardKey.Apostrophe, new[] { "SYMBOL SHIFT", "P" }));
        return list.ToArray();
    }

    private static readonly string[] FusionInputs = ["left", "right", "up", "down", "fire", "jump", "start", "coin"];
    private readonly bool[] _fusionInputs = new bool[FusionInputs.Length];

    /// <summary>
    /// Fusion controls: arrows move, Ctrl/Z fire, Space/X jump, Enter/1 start, 5/C coin
    /// (the recipe decides which game each input reaches). Gamepad works too.
    /// </summary>
    public void UpdateFusion(Retro.Fusion.IFusionRun session, bool keyboardEnabled)
    {
        bool Key(KeyboardKey k) => keyboardEnabled && Raylib.IsKeyDown(k);
        bool pad = Raylib.IsGamepadAvailable(0);
        float ax = pad ? Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftX) : 0;
        float ay = pad ? Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftY) : 0;
        bool Btn(GamepadButton b) => pad && Raylib.IsGamepadButtonDown(0, b);
        // Same order as FusionInputs; a fixed array keeps the per-frame update free of allocations.
        Span<bool> state =
        [
            Key(KeyboardKey.Left) || ax < -0.4f || Btn(GamepadButton.LeftFaceLeft),
            Key(KeyboardKey.Right) || ax > 0.4f || Btn(GamepadButton.LeftFaceRight),
            Key(KeyboardKey.Up) || ay < -0.4f || Btn(GamepadButton.LeftFaceUp),
            Key(KeyboardKey.Down) || ay > 0.4f || Btn(GamepadButton.LeftFaceDown),
            Key(KeyboardKey.LeftControl) || Key(KeyboardKey.RightControl) || Key(KeyboardKey.Z) || Btn(GamepadButton.RightFaceRight),
            Key(KeyboardKey.Space) || Key(KeyboardKey.X) || Btn(GamepadButton.RightFaceDown),
            Key(KeyboardKey.Enter) || Key(KeyboardKey.One) || Btn(GamepadButton.MiddleRight),
            Key(KeyboardKey.Five) || Key(KeyboardKey.C) || Btn(GamepadButton.MiddleLeft),
        ];
        for (int i = 0; i < state.Length; i++)
        {
            if (_fusionInputs[i] != state[i])
                session.SetInput(FusionInputs[i], state[i]);
            _fusionInputs[i] = state[i];
        }
    }

    /// <summary>
    /// Ctrl is a real key on the Spectrum (SYMBOL SHIFT), the C64 (C=) and the PC, so there the tool
    /// shortcuts also need Shift: on the Spectrum Ctrl+M alone types a full stop and Ctrl+L an equals sign.
    /// </summary>
    public static bool ToolShortcutsNeedShift(Retro.Core.IMachine? machine, bool fusion) =>
        machine is Retro.Systems.Pc.PcXt || (!fusion && machine is Spectrum48 or Retro.Systems.C64.C64);

    /// <summary>Ctrl+Shift with O, K, L or M: an emulator shortcut that must not reach the machine.</summary>
    public static bool ToolChordHeld(Func<KeyboardKey, bool> down) =>
        (down(KeyboardKey.LeftControl) || down(KeyboardKey.RightControl)) && (down(KeyboardKey.LeftShift) || down(KeyboardKey.RightShift))
        && HostToolKeys.Any(down);

    private static readonly Func<KeyboardKey, bool> KeyDown = key => Raylib.IsKeyDown(key);

    public void Update(Retro.Core.IMachine machine, bool keyboardEnabled)
    {
        if (machine is Spectrum48 zx)
            UpdateSpectrum(zx, keyboardEnabled && !ToolChordHeld(KeyDown));
        else if (machine is Retro.Systems.C64.C64 c64)
            UpdateC64(c64, keyboardEnabled && !ToolChordHeld(KeyDown));
        else if (machine is Retro.Systems.Pc.PcXt)
            Apply(machine, CollectPcKeys(KeyDown, keyboardEnabled));
        else if (machine is Retro.Systems.Nes.Nes nes)
            UpdateNes(nes, keyboardEnabled);
        else
            UpdateArcade(machine, keyboardEnabled);
    }

    private static readonly (KeyboardKey Key, string Name)[] PcKeyMap = BuildPcKeyMap();
    private static (KeyboardKey, string)[] BuildPcKeyMap()
    {
        var map = new List<(KeyboardKey, string)>();
        for (char c = 'A'; c <= 'Z'; c++) map.Add((Enum.Parse<KeyboardKey>(c.ToString()), c.ToString()));
        string[] digits = ["Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine"];
        for (int i = 0; i < 10; i++) map.Add((Enum.Parse<KeyboardKey>(digits[i]), i.ToString()));
        for (int i = 1; i <= 10; i++) map.Add((Enum.Parse<KeyboardKey>("F" + i), "F" + i));
        map.AddRange(new (KeyboardKey, string)[]
        {
            (KeyboardKey.Escape,"ESC"), (KeyboardKey.Space,"SPACE"), (KeyboardKey.Enter,"ENTER"), (KeyboardKey.KpEnter,"ENTER"),
            (KeyboardKey.Backspace,"BACKSPACE"), (KeyboardKey.Tab,"TAB"), (KeyboardKey.LeftShift,"LSHIFT"), (KeyboardKey.RightShift,"RSHIFT"),
            (KeyboardKey.LeftControl,"CTRL"), (KeyboardKey.RightControl,"CTRL"), (KeyboardKey.LeftAlt,"ALT"), (KeyboardKey.RightAlt,"ALT"),
            (KeyboardKey.Minus,"-"), (KeyboardKey.Equal,"="), (KeyboardKey.LeftBracket,"["), (KeyboardKey.RightBracket,"]"),
            (KeyboardKey.Semicolon,";"), (KeyboardKey.Apostrophe,"APOSTROPHE"), (KeyboardKey.Grave,"GRAVE"), (KeyboardKey.Backslash,"BACKSLASH"),
            (KeyboardKey.Comma,","), (KeyboardKey.Period,"."), (KeyboardKey.Slash,"/"), (KeyboardKey.CapsLock,"CAPS LOCK"),
            (KeyboardKey.NumLock,"NUM LOCK"), (KeyboardKey.ScrollLock,"SCROLL LOCK"), (KeyboardKey.Home,"HOME"), (KeyboardKey.Up,"UP"),
            (KeyboardKey.PageUp,"PAGE UP"), (KeyboardKey.Left,"LEFT"), (KeyboardKey.Right,"RIGHT"), (KeyboardKey.End,"END"),
            (KeyboardKey.Down,"DOWN"), (KeyboardKey.PageDown,"PAGE DOWN"), (KeyboardKey.Insert,"INSERT"), (KeyboardKey.Delete,"DELETE"),
            (KeyboardKey.Kp7,"HOME"), (KeyboardKey.Kp8,"UP"), (KeyboardKey.Kp9,"PAGE UP"), (KeyboardKey.Kp4,"LEFT"), (KeyboardKey.Kp5,"KP5"),
            (KeyboardKey.Kp6,"RIGHT"), (KeyboardKey.Kp1,"END"), (KeyboardKey.Kp2,"DOWN"), (KeyboardKey.Kp3,"PAGE DOWN"),
            (KeyboardKey.Kp0,"INSERT"), (KeyboardKey.KpDecimal,"DELETE"), (KeyboardKey.KpMultiply,"KP*"), (KeyboardKey.KpAdd,"KP+"), (KeyboardKey.KpSubtract,"KP-")
        });
        return map.ToArray();
    }
    private static readonly KeyboardKey[] HostFunctionKeys = [KeyboardKey.F2, KeyboardKey.F4, KeyboardKey.F5, KeyboardKey.F8, KeyboardKey.F9, KeyboardKey.F10];
    private static readonly KeyboardKey[] HostToolKeys = [KeyboardKey.O, KeyboardKey.K, KeyboardKey.L, KeyboardKey.M];
    public static HashSet<string> CollectPcKeys(Func<KeyboardKey, bool> down, bool enabled)
    {
        var now = new HashSet<string>();
        if (!enabled) return now;
        bool ctrl = down(KeyboardKey.LeftControl) || down(KeyboardKey.RightControl);
        if (ctrl && HostFunctionKeys.Any(down)) return now;
        if (ToolChordHeld(down)) return now;
        foreach (var (key, name) in PcKeyMap) if (down(key)) now.Add(name);
        return now;
    }

    /// <summary>Control port that the arrows and the gamepad drive on the C64 (most games read port 2).</summary>
    public int C64Port { get; set; } = 2;

    private static readonly (KeyboardKey key, string[] c64)[] C64KeyMap = BuildC64KeyMap();

    /// <summary>
    /// The C64 keys by position (as printed on a PC keyboard), plus Esc = RUN/STOP,
    /// Page Up = RESTORE, left Ctrl = C=, Tab = CTRL. F1 F3 F5 F7 are the C64's own keys.
    /// </summary>
    private static (KeyboardKey, string[])[] BuildC64KeyMap()
    {
        var list = new List<(KeyboardKey, string[])>();
        for (char c = 'A'; c <= 'Z'; c++)
            list.Add((Enum.Parse<KeyboardKey>(c.ToString()), new[] { c.ToString() }));
        string[] digits = { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" };
        for (int i = 0; i < 10; i++)
            list.Add((Enum.Parse<KeyboardKey>(digits[i]), new[] { i.ToString() }));
        list.Add((KeyboardKey.Space, new[] { "SPACE" }));
        list.Add((KeyboardKey.Enter, new[] { "RETURN" }));
        list.Add((KeyboardKey.KpEnter, new[] { "RETURN" }));
        list.Add((KeyboardKey.Backspace, new[] { "DEL" }));
        list.Add((KeyboardKey.Insert, new[] { "LSHIFT", "DEL" }));
        list.Add((KeyboardKey.Home, new[] { "HOME" }));
        list.Add((KeyboardKey.LeftShift, new[] { "LSHIFT" }));
        list.Add((KeyboardKey.RightShift, new[] { "RSHIFT" }));
        list.Add((KeyboardKey.LeftControl, new[] { "C=" }));
        list.Add((KeyboardKey.Tab, new[] { "CTRL" }));
        list.Add((KeyboardKey.Escape, new[] { "RUN/STOP" }));
        list.Add((KeyboardKey.PageUp, new[] { "RESTORE" }));
        list.Add((KeyboardKey.F1, new[] { "F1" }));
        list.Add((KeyboardKey.F3, new[] { "F3" }));
        list.Add((KeyboardKey.F5, new[] { "F5" }));
        list.Add((KeyboardKey.F7, new[] { "F7" }));
        list.Add((KeyboardKey.Comma, new[] { "," }));
        list.Add((KeyboardKey.Period, new[] { "." }));
        list.Add((KeyboardKey.Slash, new[] { "/" }));
        list.Add((KeyboardKey.Semicolon, new[] { ";" }));
        list.Add((KeyboardKey.Apostrophe, new[] { ":" }));
        list.Add((KeyboardKey.Minus, new[] { "-" }));
        list.Add((KeyboardKey.Equal, new[] { "=" }));
        list.Add((KeyboardKey.LeftBracket, new[] { "@" }));
        list.Add((KeyboardKey.RightBracket, new[] { "*" }));
        list.Add((KeyboardKey.Backslash, new[] { "POUND" }));
        list.Add((KeyboardKey.Grave, new[] { "ARROW LEFT" }));
        list.Add((KeyboardKey.KpAdd, new[] { "+" }));
        list.Add((KeyboardKey.KpMultiply, new[] { "*" }));
        list.Add((KeyboardKey.KpSubtract, new[] { "-" }));
        list.Add((KeyboardKey.KpDivide, new[] { "/" }));
        return list.ToArray();
    }

    private void UpdateC64(Retro.Systems.C64.C64 m, bool keyboardEnabled)
    {
        var now = new HashSet<string>();
        if (keyboardEnabled)
            foreach (var (key, c64) in C64KeyMap)
                if (Raylib.IsKeyDown(key))
                    foreach (var k in c64)
                        now.Add(k);

        bool up = false, down = false, left = false, right = false, fire = false;
        if (keyboardEnabled)
        {
            up = Raylib.IsKeyDown(KeyboardKey.Up);
            down = Raylib.IsKeyDown(KeyboardKey.Down);
            left = Raylib.IsKeyDown(KeyboardKey.Left);
            right = Raylib.IsKeyDown(KeyboardKey.Right);
            fire = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        }
        if (Raylib.IsGamepadAvailable(0))
        {
            float ax = Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftX);
            float ay = Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftY);
            left |= ax < -0.4f || Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceLeft);
            right |= ax > 0.4f || Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceRight);
            up |= ay < -0.4f || Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceUp);
            down |= ay > 0.4f || Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceDown);
            fire |= Raylib.IsGamepadButtonDown(0, GamepadButton.RightFaceDown) || Raylib.IsGamepadButtonDown(0, GamepadButton.RightFaceRight);
            // Start and Select: the keys most games ask for to begin
            if (Raylib.IsGamepadButtonDown(0, GamepadButton.MiddleRight)) now.Add("F1");
            if (Raylib.IsGamepadButtonDown(0, GamepadButton.MiddleLeft)) now.Add("SPACE");
        }
        string port = C64Port == 1 ? "JOY1 " : "JOY2 ";
        if (up) now.Add(port + "UP");
        if (down) now.Add(port + "DOWN");
        if (left) now.Add(port + "LEFT");
        if (right) now.Add(port + "RIGHT");
        if (fire) now.Add(port + "FIRE");
        Apply(m, now);
    }

    /// <summary>NES controller 1: arrows, Z = B, X = A, Enter = Start, right Shift = Select (and a gamepad).</summary>
    private void UpdateNes(Retro.Systems.Nes.Nes m, bool keyboardEnabled)
    {
        var now = new HashSet<string>();
        bool Key(KeyboardKey k) => keyboardEnabled && Raylib.IsKeyDown(k);
        bool pad = Raylib.IsGamepadAvailable(0);
        float ax = pad ? Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftX) : 0;
        float ay = pad ? Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftY) : 0;
        bool Btn(GamepadButton b) => pad && Raylib.IsGamepadButtonDown(0, b);
        if (Key(KeyboardKey.Up) || ay < -0.4f || Btn(GamepadButton.LeftFaceUp)) now.Add("P1 UP");
        if (Key(KeyboardKey.Down) || ay > 0.4f || Btn(GamepadButton.LeftFaceDown)) now.Add("P1 DOWN");
        if (Key(KeyboardKey.Left) || ax < -0.4f || Btn(GamepadButton.LeftFaceLeft)) now.Add("P1 LEFT");
        if (Key(KeyboardKey.Right) || ax > 0.4f || Btn(GamepadButton.LeftFaceRight)) now.Add("P1 RIGHT");
        if (Key(KeyboardKey.X) || Key(KeyboardKey.Space) || Btn(GamepadButton.RightFaceRight)) now.Add("P1 A");
        if (Key(KeyboardKey.Z) || Key(KeyboardKey.LeftControl) || Btn(GamepadButton.RightFaceDown)) now.Add("P1 B");
        if (Key(KeyboardKey.Enter) || Key(KeyboardKey.KpEnter) || Btn(GamepadButton.MiddleRight)) now.Add("P1 START");
        if (Key(KeyboardKey.RightShift) || Key(KeyboardKey.Tab) || Btn(GamepadButton.MiddleLeft)) now.Add("P1 SELECT");
        Apply(m, now);
    }

    /// <summary>Arcade panel: coin, start buttons, left/right and fire (also on a gamepad).</summary>
    private void UpdateArcade(Retro.Core.IMachine m, bool keyboardEnabled)
    {
        var now = new HashSet<string>();
        bool Key(KeyboardKey k) => keyboardEnabled && Raylib.IsKeyDown(k);
        bool pad = Raylib.IsGamepadAvailable(0);
        float ax = pad ? Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftX) : 0;
        bool left = Key(KeyboardKey.Left) || Key(KeyboardKey.A) || ax < -0.4f || (pad && Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceLeft));
        bool right = Key(KeyboardKey.Right) || Key(KeyboardKey.D) || ax > 0.4f || (pad && Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceRight));
        bool fire = Key(KeyboardKey.Space) || Key(KeyboardKey.LeftControl) || Key(KeyboardKey.LeftAlt) ||
                    (pad && (Raylib.IsGamepadButtonDown(0, GamepadButton.RightFaceDown) || Raylib.IsGamepadButtonDown(0, GamepadButton.RightFaceRight)));
        if (Key(KeyboardKey.Five) || Key(KeyboardKey.C) || (pad && Raylib.IsGamepadButtonDown(0, GamepadButton.MiddleLeft))) now.Add("COIN");
        if (Key(KeyboardKey.One) || (pad && Raylib.IsGamepadButtonDown(0, GamepadButton.MiddleRight))) now.Add("P1 START");
        if (Key(KeyboardKey.Two)) now.Add("P2 START");
        if (Key(KeyboardKey.T)) now.Add("TILT");
        // In a two player game the players take turns on the same controls (upright cabinet).
        if (left) { now.Add("P1 LEFT"); now.Add("P2 LEFT"); }
        if (right) { now.Add("P1 RIGHT"); now.Add("P2 RIGHT"); }
        if (fire) { now.Add("P1 FIRE"); now.Add("P2 FIRE"); }
        Apply(m, now);
    }

    private void Apply(Retro.Core.IMachine m, HashSet<string> now)
    {
        foreach (var k in _previous)
            if (!now.Contains(k))
                m.SetKey(k, false);
        foreach (var k in now)
            if (!_previous.Contains(k))
                m.SetKey(k, true);
        _previous = now;
    }

    private void UpdateSpectrum(Spectrum48 m, bool keyboardEnabled)
    {
        var now = new HashSet<string>();
        if (keyboardEnabled)
        {
            foreach (var (key, zx) in KeyMap)
                if (Raylib.IsKeyDown(key))
                    foreach (var k in zx)
                        now.Add(k);
        }

        bool up = false, down = false, left = false, right = false, fire = false;
        if (keyboardEnabled)
        {
            up = Raylib.IsKeyDown(KeyboardKey.Up);
            down = Raylib.IsKeyDown(KeyboardKey.Down);
            left = Raylib.IsKeyDown(KeyboardKey.Left);
            right = Raylib.IsKeyDown(KeyboardKey.Right);
            fire = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt) || Raylib.IsKeyDown(KeyboardKey.Tab);
        }
        if (Raylib.IsGamepadAvailable(0))
        {
            float ax = Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftX);
            float ay = Raylib.GetGamepadAxisMovement(0, GamepadAxis.LeftY);
            left |= ax < -0.4f || Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceLeft);
            right |= ax > 0.4f || Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceRight);
            up |= ay < -0.4f || Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceUp);
            down |= ay > 0.4f || Raylib.IsGamepadButtonDown(0, GamepadButton.LeftFaceDown);
            fire |= Raylib.IsGamepadButtonDown(0, GamepadButton.RightFaceDown) || Raylib.IsGamepadButtonDown(0, GamepadButton.RightFaceRight);
        }

        switch (Joystick)
        {
            case JoystickMode.Kempston:
                if (up) now.Add("KEMPSTON UP");
                if (down) now.Add("KEMPSTON DOWN");
                if (left) now.Add("KEMPSTON LEFT");
                if (right) now.Add("KEMPSTON RIGHT");
                if (fire) now.Add("KEMPSTON FIRE");
                break;
            case JoystickMode.Cursor:      // 5 6 7 8 and 0, as the cursor keys print them
                if (left) now.Add("5");
                if (down) now.Add("6");
                if (up) now.Add("7");
                if (right) now.Add("8");
                if (fire) now.Add("0");
                break;
            case JoystickMode.Sinclair1:   // Interface 2 port 1: 6 7 8 9 0
                if (left) now.Add("6");
                if (right) now.Add("7");
                if (down) now.Add("8");
                if (up) now.Add("9");
                if (fire) now.Add("0");
                break;
        }

        Apply(m, now);
    }

    public void ReleaseAll(Retro.Core.IMachine m)
    {
        foreach (var k in _previous)
            m.SetKey(k, false);
        _previous.Clear();
    }
}
