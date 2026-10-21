using Retro.Fusion;

namespace Retro.Studio;

/// <summary>
/// What fusion scripts can do, and nothing else: the methods here are the only bridge
/// between the sandboxed JavaScript and the emulator. The prelude wraps them into the
/// friendly API documented in Docs/API.md (games, screen, ui, sound...).
/// </summary>
public sealed class ScriptHost
{
    private readonly StudioSession _session;
    private readonly List<SpriteImage> _sprites = new();
    private readonly Dictionary<string, int> _capturedPixels = new();

    internal ScriptHost(StudioSession session) => _session = session;
    internal void ResetSprites() { _sprites.Clear(); _capturedPixels.Clear(); }

    private StudioSession.SlotState S(string id) => _session.SlotFor(id);

    private static int Num(double v) => double.IsFinite(v) ? (int)Math.Floor(v) : 0;

    // ------------------------------------------------------------------ games
    public string SlotIds() => string.Join(",", _session.SlotStates.Select(s => s.Slot.Id));
    public string Name(string id) => S(id).Slot.Profile.Name;
    public string ProfileId(string id) => S(id).Slot.Profile.Id;
    public string Machine(string id) => S(id).Slot.Profile.Machine;
    public int Width(string id) => S(id).Slot.Machine.Video.Width;
    public int Height(string id) => S(id).Slot.Machine.Video.Height;
    public int AreaX(string id) => StudioSession.PictureArea(S(id).Slot.Machine).x;
    public int AreaY(string id) => StudioSession.PictureArea(S(id).Slot.Machine).y;
    public int AreaW(string id) => StudioSession.PictureArea(S(id).Slot.Machine).w;
    public int AreaH(string id) => StudioSession.PictureArea(S(id).Slot.Machine).h;

    public int Peek(string id, double address)
    {
        _session.ReferencedSlots.Add(id);
        return S(id).Slot.Machine.Memory.Peek((uint)Num(address) & 0xFFFF);
    }

    public void Poke(string id, double address, double value) =>
        S(id).Slot.Machine.Memory.Poke((uint)Num(address) & 0xFFFF, (byte)Num(value));

    private static GameVariable Variable(StudioSession.SlotState s, string name) =>
        s.Slot.Profile.Variables.TryGetValue(name, out var v) ? v
            : throw new ArgumentException($"{s.Slot.Profile.Name} no tiene la variable \"{name}\". Tiene: {string.Join(", ", s.Slot.Profile.Variables.Keys)}.");

    public int Get(string id, string name)
    {
        _session.ReferencedSlots.Add(id);
        var s = S(id);
        return Variable(s, name).Read(s.Slot.Machine.Memory);
    }

    public void Set(string id, string name, double value)
    {
        _session.ReferencedSlots.Add(id);
        var s = S(id);
        var v = Variable(s, name);
        if (v.ReadOnly)
            throw new ArgumentException($"La variable \"{name}\" de {s.Slot.Profile.Name} es de solo lectura (usá peek/poke si de verdad hace falta).");
        v.Write(s.Slot.Machine.Memory, Num(value));
    }

    private static string KeyFor(StudioSession.SlotState s, string name)
    {
        if (s.Slot.Profile.Controls.TryGetValue(name, out var key))
            return key;
        if (s.Slot.Machine.Keys.Any(k => k.Name == name))
            return name;
        throw new ArgumentException($"{s.Slot.Profile.Name} no tiene el control \"{name}\". Controles: {string.Join(", ", s.Slot.Profile.Controls.Keys)}; " +
                                    $"teclas: {string.Join(", ", s.Slot.Machine.Keys.Select(k => k.Name).Take(60))}.");
    }

    public void Control(string id, string name, bool down)
    {
        _session.ReferencedSlots.Add(id);
        var s = S(id);
        s.Slot.Machine.SetKey(KeyFor(s, name), down);
    }

    public void Tap(string id, string name, double frames)
    {
        _session.ReferencedSlots.Add(id);
        var s = S(id);
        s.Slot.Machine.QueueKeys(Math.Clamp(Num(frames), 1, 600), KeyFor(s, name));
    }

    public void Pause(string id, bool on) => S(id).Paused = on;
    public bool IsPaused(string id) => S(id).Paused;
    public double GetSpeed(string id) => S(id).Speed;
    public void SetSpeed(string id, double v) => S(id).Speed = double.IsFinite(v) ? Math.Clamp(v, 0, 4) : 1;
    public double GetVolume(string id) => S(id).Volume;
    public void SetVolume(string id, double v) => S(id).Volume = double.IsFinite(v) ? Math.Clamp(v, 0, 2) : 1;
    public double Frame(string id) => S(id).Slot.Machine.FrameCount;

    public void Restart(string id)
    {
        var s = S(id);
        s.Slot.Machine.LoadState(s.BootState);
        s.BootPicture.Pixels.CopyTo(s.Slot.Machine.Video.Pixels, 0);
        s.Slot.Machine.ReleaseAllKeys();
        s.Accumulator = 0;
        s.Audio.Clear();
    }

    public int Pixel(string id, double x, double y)
    {
        _session.ReferencedSlots.Add(id);
        var v = S(id).Slot.Machine.Video;
        int px = Num(x), py = Num(y);
        return (uint)px < (uint)v.Width && (uint)py < (uint)v.Height ? v.Pixels[py * v.Width + px] & 0xFFFFFF : 0;
    }

    public int Sprite(string id, string name)
    {
        _session.ReferencedSlots.Add(id);
        var s = S(id);
        if (!s.Slot.Profile.Sprites.TryGetValue(name, out var def))
            throw new ArgumentException($"{s.Slot.Profile.Name} no tiene el sprite \"{name}\". Tiene: {string.Join(", ", s.Slot.Profile.Sprites.Keys)}.");
        return AddSprite(def, s);
    }

    public int SpriteAt(string id, double address, double width, double height, string layout)
    {
        _session.ReferencedSlots.Add(id);
        var def = new SpriteDef
        {
            Address = (Num(address) & 0xFFFF).ToString("X4"),
            Width = Math.Clamp(Num(width), 1, 64),
            Height = Math.Clamp(Num(height), 1, 64),
            Layout = layout == "columns" ? "columns" : "rows",
        };
        return AddSprite(def, S(id));
    }

    public int Capture(string id, double x, double y, double width, double height, double transparent)
    {
        var slot = S(id);
        var machine = slot.Slot.Machine;
        var area = StudioSession.PictureArea(machine);
        int px = Num(x), py = Num(y), w = Num(width), h = Num(height);
        if (w is < 1 or > 32 || h is < 1 or > 32 || px < 0 || py < 0 || px > area.w - w || py > area.h - h
            || _capturedPixels.GetValueOrDefault(id) + w * h > 4096)
        {
            _session.SceneViolation = true;
            _session.SceneProblem = "g.capture admite recursos de hasta 32x32 dentro del área activa y 4096 píxeles por original, " +
                "sumados durante toda la partida. Extraé personajes o tiles una vez y reutilizalos; no reconstruyas pantallas con recortes.";
            throw new InvalidOperationException(_session.SceneProblem);
        }
        if (_sprites.Count >= 512) throw new InvalidOperationException("Demasiados sprites: guardalos y reutilizalos.");
        var colors = new int[w * h];
        var mask = new bool[w * h];
        int key = transparent < 0 ? -1 : Num(transparent) & 0xFFFFFF;
        for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
            {
                int index = row * w + col;
                colors[index] = machine.Video.Pixels[(area.y + py + row) * machine.Video.Width + area.x + px + col] & 0xFFFFFF;
                mask[index] = colors[index] != key;
            }
        _capturedPixels[id] = _capturedPixels.GetValueOrDefault(id) + w * h;
        _session.ReferencedSlots.Add(id);
        _sprites.Add(new SpriteImage(w, h, mask, colors));
        return _sprites.Count - 1;
    }

    private int AddSprite(SpriteDef def, StudioSession.SlotState s)
    {
        if (_sprites.Count >= 512)
            throw new InvalidOperationException("Demasiados sprites: pedilos una vez en init() y guardalos en variables.");
        _sprites.Add(new SpriteImage(def.Width, def.Height, def.Decode(s.Slot.Machine.Memory)));
        return _sprites.Count - 1;
    }

    private SpriteImage SpriteImage(double handle)
    {
        int i = Num(handle);
        return (uint)i < (uint)_sprites.Count ? _sprites[i] : throw new ArgumentException("Ese sprite no existe: pedilo con g.sprite(nombre).");
    }

    public int SpriteW(double handle) => SpriteImage(handle).Width;
    public int SpriteH(double handle) => SpriteImage(handle).Height;

    // ------------------------------------------------------------------ screen
    public void Size(double w, double h)
    {
        if (_session.Game.BaseSlot is { } id)
        {
            var area = StudioSession.PictureArea(S(id).Slot.Machine);
            if (Num(w) != area.w || Num(h) != area.h)
            {
                RejectScene($"El modo base conserva su resolución ({area.w}x{area.h}). Integrá los aportes dentro de ese mundo; la creación libre permite otra escena.");
                return;
            }
        }
        _session.Canvas.Resize(Num(w), Num(h));
    }
    public int CanvasW() => _session.Canvas.Width;
    public int CanvasH() => _session.Canvas.Height;
    public void Clear(double color)
    {
        if (_session.Game.BaseSlot != null && _session.IsDrawing)
        {
            RejectScene("En modo base, el motor ya dibuja la imagen original antes de draw(). No uses screen.clear para reemplazarla: integrá personajes y mecánicas sobre ese mundo.");
            return;
        }
        _session.Canvas.Clear(Num(color));
    }

    private void RejectScene(string problem)
    {
        _session.SceneViolation = true; _session.SceneProblem = problem;
        throw new InvalidOperationException(problem);
    }
    public void Rect(double x, double y, double w, double h, double color, double alpha) => _session.Canvas.Rect(x, y, w, h, Num(color), alpha);
    public void Box(double x, double y, double w, double h, double color) => _session.Canvas.Box(x, y, w, h, Num(color));
    public void Line(double x0, double y0, double x1, double y1, double color) => _session.Canvas.Line(x0, y0, x1, y1, Num(color));
    public void Text(string text, double x, double y, double color, double scale, bool center) => _session.Canvas.Text(text, x, y, Num(color), Num(scale), center);

    public void DrawSprite(double handle, double x, double y, double color, double scale, bool flip, double alpha) =>
        _session.Canvas.Sprite(SpriteImage(handle), x, y, Num(color), Num(scale), flip, alpha);

    // Keep a diagnostic for old scripts, with no route to copy a game framebuffer.
    public void RejectGamePicture()
    {
        _session.SceneViolation = true;
        throw new InvalidOperationException(StudioValidator.OwnSceneMessage);
    }

    public void DrawBase(string id)
    {
        if (_session.Game.BaseSlot != id)
        {
            _session.SceneViolation = true;
            _session.SceneProblem = "screen.base solo admite el original elegido como base. Conservá ese mundo e integrá los otros mediante sus sprites, datos y reglas; no pegues sus pantallas completas.";
            throw new InvalidOperationException(_session.SceneProblem);
        }
        var machine = S(id).Slot.Machine;
        var area = StudioSession.PictureArea(machine);
        _session.Canvas.BaseFrame(machine.Video, area.x, area.y, area.w, area.h);
    }

    // ------------------------------------------------------------------ ui and sound
    public void Message(string text) => _session.AddMessage(text.Length > 120 ? text[..120] : text);
    public void Hud(string text) => _session.Hud = text.Length > 160 ? text[..160] : text;
    public void Log(string text) => _session.AddLog(text);
    public void GameState(string state) => _session.SetState(state);
    public void GameScore(double points) => _session.AddScore(Num(points));
    public void GameDeath(string cause) => _session.AddDeath(cause);
    public void GameLevel(double level) => _session.SetLevel(Num(level));

    public void Beep(double frequency, double milliseconds, double volume) => _session.AddTone(frequency, milliseconds, volume, false);
    public void Noise(double milliseconds, double volume) => _session.AddTone(100, milliseconds, volume, true);

    /// <summary>The JavaScript API every fusion script sees (documented in Docs/API.md).</summary>
    public const string Prelude = """
        var games = {};
        (function () {
          __h.SlotIds().split(',').forEach(function (id) {
            var g = {
              id: id,
              name: __h.Name(id),
              profile: __h.ProfileId(id),
              machine: __h.Machine(id),
              width: __h.Width(id),
              height: __h.Height(id),
              area: { x: __h.AreaX(id), y: __h.AreaY(id), w: __h.AreaW(id), h: __h.AreaH(id) },
              peek: function (a) { return __h.Peek(id, a); },
              peek16: function (a) { return __h.Peek(id, a) | (__h.Peek(id, a + 1) << 8); },
              poke: function (a, v) { __h.Poke(id, a, v); },
              get: function (name) { return __h.Get(id, name); },
              set: function (name, v) { __h.Set(id, name, v); },
              control: function (name, down) { __h.Control(id, name, !!down); },
              key: function (name, down) { __h.Control(id, name, !!down); },
              tap: function (name, frames) { __h.Tap(id, name, frames === undefined ? 4 : frames); },
              pause: function (on) { __h.Pause(id, on === undefined ? true : !!on); },
              resume: function () { __h.Pause(id, false); },
              restart: function () { __h.Restart(id); },
              pixel: function (x, y) { return __h.Pixel(id, x, y); },
              sprite: function (name) { return __h.Sprite(id, name); },
              capture: function (x, y, w, h, transparent) { return __h.Capture(id, x, y, w, h, transparent === undefined ? -1 : transparent); },
              spriteAt: function (address, w, h, layout) { return __h.SpriteAt(id, address, w, h, layout || 'rows'); }
            };
            Object.defineProperty(g, 'paused', { get: function () { return __h.IsPaused(id); } });
            Object.defineProperty(g, 'frame', { get: function () { return __h.Frame(id); } });
            Object.defineProperty(g, 'speed', { get: function () { return __h.GetSpeed(id); }, set: function (v) { __h.SetSpeed(id, v); } });
            Object.defineProperty(g, 'volume', { get: function () { return __h.GetVolume(id); }, set: function (v) { __h.SetVolume(id, v); } });
            games[id] = g;
          });
        })();

        var screen = {
          size: function (w, h) { __h.Size(w, h); },
          clear: function (c) { __h.Clear(c === undefined ? 0 : c); },
          rect: function (x, y, w, h, c, alpha) { __h.Rect(x, y, w, h, c === undefined ? 0xFFFFFF : c, alpha === undefined ? 1 : alpha); },
          box: function (x, y, w, h, c) { __h.Box(x, y, w, h, c === undefined ? 0xFFFFFF : c); },
          line: function (x0, y0, x1, y1, c) { __h.Line(x0, y0, x1, y1, c === undefined ? 0xFFFFFF : c); },
          text: function (s, x, y, c, o) { o = o || {}; __h.Text(String(s), x, y, c === undefined ? 0xFFFFFF : c, o.scale || 1, !!o.center); },
          sprite: function (s, x, y, c, o) {
            o = o || {};
            __h.DrawSprite(s, x, y, c === undefined ? -1 : c, o.scale || 1, !!o.flip, o.alpha === undefined ? 1 : o.alpha);
          },
          spriteSize: function (s) { return { w: __h.SpriteW(s), h: __h.SpriteH(s) }; },
          base: function (g) { __h.DrawBase(typeof g === 'string' ? g : g.id); },
          game: function () { __h.RejectGamePicture(); }
        };
        Object.defineProperty(screen, 'width', { get: function () { return __h.CanvasW(); } });
        Object.defineProperty(screen, 'height', { get: function () { return __h.CanvasH(); } });

        var ui = {
          message: function (t) { __h.Message(String(t)); },
          hud: function (t) { __h.Hud(String(t)); }
        };
        var game = {
          state: function (s) { __h.GameState(String(s)); },
          score: function (points) { __h.GameScore(points === undefined ? 1 : points); },
          death: function (cause) { __h.GameDeath(String(cause === undefined ? 'sin causa' : cause)); },
          level: function (n) { __h.GameLevel(n); }
        };
        var sound = {
          beep: function (freq, ms, vol) { __h.Beep(freq, ms === undefined ? 100 : ms, vol === undefined ? 0.4 : vol); },
          noise: function (ms, vol) { __h.Noise(ms === undefined ? 150 : ms, vol === undefined ? 0.4 : vol); }
        };
        function log() { __h.Log(Array.prototype.map.call(arguments, function (a) { return typeof a === 'object' ? JSON.stringify(a) : String(a); }).join(' ')); }
        function rnd(n) { return Math.floor(Math.random() * n); }
        function clamp(v, lo, hi) { return v < lo ? lo : v > hi ? hi : v; }
        function overlap(a, b) { return a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h; }

        var __names = ['left', 'right', 'up', 'down', 'fire', 'jump', 'start', 'coin'];
        var __prev = 0;
        var __tickCount = 0;
        function __init() { if (typeof init === 'function') init(); }
        function __tick(mask) {
          var input = { pressed: {} };
          for (var i = 0; i < __names.length; i++) {
            var on = ((mask >> i) & 1) === 1;
            input[__names[i]] = on;
            input.pressed[__names[i]] = on && ((__prev >> i) & 1) === 0;
          }
          __prev = mask;
          __tickCount++;
          input.tick = __tickCount;
          if (typeof tick === 'function') tick(input);
        }
        function __draw() { if (typeof draw === 'function') draw(); }
        """;
}
