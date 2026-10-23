using Retro.Ai;
using Retro.Studio;

namespace Studio.Tests;

[TestFixture, NonParallelizable]
public class AgentWorkspaceTests
{
    [Test]
    public void CreatingAnotherTaskForTheSameGamePreservesThePreviousWorkInBothCreators()
    {
        string root = Path.Combine(Path.GetTempPath(), "retro-workspace-" + Guid.NewGuid().ToString("N"));
        string previousStudio = StudioWorkspace.Root, previousCreator = GameWorkspace.Root;
        try
        {
            StudioWorkspace.Root = Path.Combine(root, "studio"); GameWorkspace.Root = Path.Combine(root, "creator");
            var game = new StudioGame { Id = "own-game", Title = "Juego propio", Script = "function init(){}" };
            string first = StudioWorkspace.Create(game, "Primer trabajo", _ => null, null);
            File.WriteAllText(Path.Combine(first, StudioWorkspace.ScriptFile), "// trabajo pendiente del jugador");
            File.WriteAllText(Path.Combine(first, StudioWorkspace.ReportFile), "Informe anterior");
            string second = StudioWorkspace.Create(game, "Otro trabajo", _ => null, null);
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(File.ReadAllText(Path.Combine(first, StudioWorkspace.ScriptFile)), Is.EqualTo("// trabajo pendiente del jugador"));
            Assert.That(File.ReadAllText(Path.Combine(first, StudioWorkspace.ReportFile)), Is.EqualTo("Informe anterior"));
            first = GameWorkspace.Create("zx48", "Primera idea", new Random(1), null);
            File.WriteAllText(Path.Combine(first, GameWorkspace.SourceFile), "; trabajo pendiente");
            second = GameWorkspace.Create("zx48", "Segunda idea", new Random(1), null);
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(File.ReadAllText(Path.Combine(first, GameWorkspace.SourceFile)), Is.EqualTo("; trabajo pendiente"));
        }
        finally
        {
            StudioWorkspace.Root = previousStudio; GameWorkspace.Root = previousCreator;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}