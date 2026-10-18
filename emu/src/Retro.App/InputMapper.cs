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

    private readonly Dictionary<string, bool> _fusionInputs = new();

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
        var state = new Dictionary<string, bool>
        {
            ["left"] = Key(KeyboardKey.Left) || ax < -0.4f || Btn(GamepadButton.LeftFaceLeft),
            ["right"] = Key(KeyboardKey.Right) || ax > 0.4f || Btn(GamepadButton.LeftFaceRight),
            ["up"] = Key(KeyboardKey.Up) || ay < -0.4f || Btn(GamepadButton.LeftFaceUp),
            ["down"] = Key(KeyboardKey.Down) || ay > 0.4f || Btn(GamepadButton.LeftFaceDown),
            ["fire"] = Key(KeyboardKey.LeftControl) || Key(KeyboardKey.RightControl) || Key(KeyboardKey.Z) || Btn(GamepadButton.RightFaceRight),
            ["jump"] = Key(KeyboardKey.Space) || Key(KeyboardKey.X) || Btn(GamepadButton.RightFaceDown),
            ["start"] = Key(KeyboardKey.Enter) || Key(KeyboardKey.One) || Btn(GamepadButton.MiddleRight),
            ["coin"] = Key(KeyboardKey.Five) || Key(KeyboardKey.C) || Btn(GamepadButton.MiddleLeft),
        };
        foreach (var (input, pressed) in state)
        {
            _fusionInputs.TryGetValue(input, out bool was);
            if (was != pressed)
                session.SetInput(input, pressed);
            _fusionInputs[input] = pressed;
        }
    }

    public void Update(Retro.Core.IMachine machine, bool keyboardEnabled)
    {
        if (machine is Spectrum48 zx)
            UpdateSpectrum(zx, keyboardEnabled);
        else if (machine is Retro.Systems.C64.C64 c64)
            UpdateC64(c64, keyboardEnabled);
        else
            UpdateArcade(machine, keyboardEnabled);
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
