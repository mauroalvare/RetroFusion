namespace Retro.Cpu.Z80.Tests;

internal static class TestPaths
{
    /// <summary>The emu/ folder (contains testdata/ and roms/).</summary>
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "testdata")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("No se encontró la carpeta testdata/");
        }
    }
}
