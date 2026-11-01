using System.Collections;
using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Retro.Ai;
using Retro.App;
using Retro.Core;
using Retro.Machines;
using Retro.Studio;

namespace App.Tests;

/// <summary>Actual ImGui mouse, wheel and text events against the library; no physical-controller claim.</summary>
[TestFixture, NonParallelizable]
public sealed class LibraryInteractionTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [TestCase(1280, 840, 1), TestCase(640, 480, 2)]
    public void FiltersEditableInfoResetAndPlayingAreReachableWithRealClicks(int width, int height, int scale)
    {
        using var view = new View(width, height, scale);
        view.Home.ShowOriginals(); view.Frame(); view.Frame();
        Assert.That(view.Keys(), Has.Count.EqualTo(3));
        view.Click("Filtros"); view.Click("Género"); view.Click("Elegir Género:Plataformas");
        Assert.That(view.Keys(), Is.EqualTo(new[] { view.First }));
        view.Click("Ver resultados"); view.Click("Ficha:" + view.First);
        view.Type("Géneros de la ficha", "Aventura, Puzles"); view.Type("Año de la ficha", "1991");
        view.Click("Guardar ficha"); view.Frame();
        Assert.That(view.Keys(), Is.Empty, "The edited game no longer matches the old genre filter.");
        var saved = new LibraryMetadata(view.MetadataPath).Get(view.First);
        Assert.That(saved.Genres, Is.EqualTo(new[] { "Aventura", "Puzles" })); Assert.That(saved.ReleaseYear, Is.EqualTo(1991));
        view.Click("Quitar filtros"); view.Frame(); Assert.That(view.Keys(), Has.Count.EqualTo(3));
        view.Click("Jugar:" + view.First);
        Assert.That(view.Played, Is.EqualTo(new[] { view.FirstPath })); Assert.That(view.Home.IsOpen, Is.False);
        Assert.That(File.ReadAllBytes(view.FirstPath), Is.EqualTo(view.OriginalBytes));
    }

    [TestCase(1280, 840, 1), TestCase(640, 480, 2)]
    public void FavoritesRecentOrderAndTheMetadataCancelDoNotLoseTheCurrentSelection(int width, int height, int scale)
    {
        using var view = new View(width, height, scale);
        view.Activity.SetFavorite(view.First, true); view.Home.ShowFavorites(); view.Frame(); view.Frame();
        Assert.That(view.Keys(), Is.EqualTo(new[] { view.First }));
        view.Click("Favorito:" + view.First); view.Frame(); Assert.That(view.Keys(), Is.Empty);
        view.Home.ShowOriginals(); view.Frame(); byte[] before = File.ReadAllBytes(view.MetadataPath);
        view.Click("Ficha:" + view.First); view.Type("Géneros de la ficha", "Carreras"); view.Click("Cancelar ficha");
        Assert.That(File.ReadAllBytes(view.MetadataPath), Is.EqualTo(before));
        view.Clock.Now = view.Clock.Now.AddDays(-2); view.Activity.Played(view.First);
        view.Clock.Now = view.Clock.Now.AddDays(1); view.Activity.Played(view.Second);
        view.Clock.Now = view.Clock.Now.AddDays(1); view.Home.ShowRecents(); view.Frame(); view.Frame();
        Assert.That(view.Keys(), Is.EqualTo(new[] { view.Second, view.First }));
        Assert.That(view.Played, Is.Empty, "Filtering and editing never launch a game.");
    }

    [Test]
    public void DuplicateOriginalPathsKeepTheirActionsButDoNotDuplicateFavoritesOrRecents()
    {
        using var view = new View(1280, 840, 1);
        var entries = (List<LibraryEntry>)typeof(GameLibrary).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view.Library)!;
        entries.Add(entries[0] with { Path = Path.Combine(Path.GetDirectoryName(view.FirstPath)!, "copy.prg"), Name = "Second path" });
        view.Home.ShowOriginals(); view.Frame(); Assert.That(view.Keys().Count(key => key == view.First), Is.EqualTo(2));
        view.Activity.SetFavorite(view.First, true); view.Home.ShowFavorites(); view.Frame(); Assert.That(view.Keys(), Is.EqualTo(new[] { view.First }));
        view.Activity.Played(view.First); view.Home.ShowRecents(); view.Frame(); Assert.That(view.Keys(), Is.EqualTo(new[] { view.First }));
    }

    private sealed class View : IDisposable
    {
        private readonly string _folder, _previousFusion, _previousCreated;
        private readonly string? _previousProfiles;
        private readonly nint _context;
        private readonly float _menu, _navigation;
        private readonly Dictionary<string, (Vector2 Min, Vector2 Max, Vector2 ClipMin, Vector2 ClipMax)> _bounds = new();
        public readonly Clock Clock = new();
        public readonly LibraryActivity Activity;
        public readonly GameLibrary Library;
        public readonly HomeScreen Home;
        public readonly List<string> Played = [];
        public readonly byte[] OriginalBytes = [1, 8, 0x60, 1];
        public string First { get; }
        public string Second { get; }
        public string FirstPath { get; }
        public string MetadataPath => Path.Combine(_folder, "catalogo.json");

        public View(int width, int height, int scale)
        {
            _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-library-click-" + Guid.NewGuid().ToString("N"))).FullName;
            _previousFusion = StudioLibrary.UserFolder; _previousCreated = CreatedGames.UserFolder;
            _previousProfiles = Environment.GetEnvironmentVariable("RETROEMU_PROFILES");
            StudioLibrary.UserFolder = Path.Combine(_folder, "fusion"); CreatedGames.UserFolder = Path.Combine(_folder, "created");
            Environment.SetEnvironmentVariable("RETROEMU_PROFILES", Path.Combine(_folder, "profiles"));
            _context = ImGui.CreateContext(); var io = ImGui.GetIO(); io.DisplaySize = new(width, height); io.DeltaTime = 1f / 60;
            unsafe { io.NativePtr->IniFilename = null; }
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            io.Fonts.AddFontDefault(); io.Fonts.GetTexDataAsRGBA32(out nint _, out int _, out int _, out int _); UiTheme.Apply(scale, false);
            _menu = WorkspaceLayout.MenuHeight; _navigation = WorkspaceLayout.NavigationHeight;
            WorkspaceLayout.MenuHeight = 28; WorkspaceLayout.NavigationHeight = 0;
            var library = Library = new GameLibrary(); var games = new List<LibraryEntry>();
            foreach (var (name, machine, number) in new[] { ("Faro", "zx48", 1), ("Ruedas", "nes", 2), ("Taller", "c64", 3) })
            {
                byte[] bytes = [1, 8, 0x60, (byte)number]; string path = Path.Combine(_folder, name + ".prg"); File.WriteAllBytes(path, bytes);
                string key = LibraryKey.Original(machine, [new MediaFile(name + ".prg", bytes, path)]);
                games.Add(new(path, name, MachineCatalog.Get(machine), 100, "own fixture", Identity: key));
            }
            First = games[0].Identity; Second = games[1].Identity; FirstPath = games[0].Path;
            typeof(GameLibrary).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(library, games);
            var metadata = new LibraryMetadata(MetadataPath, clock: Clock);
            metadata.Update(First, ["Aventura", "Plataformas"], 1984); metadata.Update(Second, ["Carreras"], 1990);
            Activity = new LibraryActivity(Path.Combine(_folder, "activity.json"), Clock);
            Home = new HomeScreen(library, Played.Add, _ => Assert.Fail("An original must not become a fusion."), () => { }, _ => { }, () => false,
                Activity, metadata: metadata, clock: Clock);
            Home.LibraryControlBounds = (name, min, max) => _bounds[name] = (min, max,
                ImGui.GetWindowDrawList().GetClipRectMin(), ImGui.GetWindowDrawList().GetClipRectMax());
        }

        public void Frame() { _bounds.Clear(); ImGui.GetIO().DeltaTime = 1f / 60; ImGui.NewFrame(); Home.Draw(); ImGui.Render(); }

        public List<string> Keys()
        {
            var cards = (IEnumerable)typeof(HomeScreen).GetMethod("FilteredCards", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Home, null)!;
            return cards.Cast<object>().Select(card => (string)card.GetType().GetProperty("Key")!.GetValue(card)!).ToList();
        }

        public void Click(string label)
        {
            var io = ImGui.GetIO();
            for (int attempt = 0; attempt < 80; attempt++)
            {
                Frame();
                if (!_bounds.ContainsKey(label))
                {
                    io.AddMousePosEvent(io.DisplaySize.X / 2, io.DisplaySize.Y / 2); io.AddMouseWheelEvent(0, -0.5f); Frame(); continue;
                }
                var bounds = _bounds[label]; Vector2 center = (bounds.Min + bounds.Max) / 2;
                if (bounds.Min.X >= bounds.ClipMin.X && bounds.Max.X <= bounds.ClipMax.X && bounds.Min.Y >= bounds.ClipMin.Y && bounds.Max.Y <= bounds.ClipMax.Y)
                {
                    io.AddMousePosEvent(center.X, center.Y); Frame(); io.AddMouseButtonEvent(0, true); Frame(); io.AddMouseButtonEvent(0, false); Frame(); return;
                }
                Vector2 scrollPoint = (bounds.ClipMin + bounds.ClipMax) / 2;
                io.AddMousePosEvent(scrollPoint.X, scrollPoint.Y); io.AddMouseWheelEvent(0, bounds.Max.Y > bounds.ClipMax.Y ? -0.5f : 0.5f); Frame();
            }
            Assert.Fail("Control outside the reachable area: " + label);
        }

        public void Type(string label, string text)
        {
            Click(label); var io = ImGui.GetIO();
            io.AddKeyEvent(ImGuiKey.ModCtrl, true); io.AddKeyEvent(ImGuiKey.A, true); Frame();
            io.AddKeyEvent(ImGuiKey.A, false); io.AddKeyEvent(ImGuiKey.ModCtrl, false); Frame();
            io.AddInputCharactersUTF8(text); Frame();
            io.AddKeyEvent(ImGuiKey.Tab, true); Frame(); io.AddKeyEvent(ImGuiKey.Tab, false); Frame();
        }

        public void Dispose()
        {
            Home.Dispose(); UiTheme.Apply(1, false); ImGui.DestroyContext(_context);
            WorkspaceLayout.MenuHeight = _menu; WorkspaceLayout.NavigationHeight = _navigation;
            StudioLibrary.UserFolder = _previousFusion; CreatedGames.UserFolder = _previousCreated;
            Environment.SetEnvironmentVariable("RETROEMU_PROFILES", _previousProfiles);
            Assert.That(Path.GetDirectoryName(Path.GetFullPath(_folder)), Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
            Assert.That(Path.GetFileName(_folder), Does.StartWith("retro-library-click-")); Directory.Delete(_folder, true);
        }
    }
}
