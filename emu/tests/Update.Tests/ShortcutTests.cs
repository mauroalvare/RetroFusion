using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Retro.Update;

namespace Update.Tests;

[TestFixture]
public class ShortcutTests
{
    [Test, Platform("Win")]
    public void ExistingOwnedShortcutFollowsVersionAndCanBeRestored()
    {
        if (OperatingSystem.IsWindows()) CheckWindows();
    }
    [SupportedOSPlatform("windows")]
    private static void CheckWindows()
    {
        string root = Path.Combine(Path.GetTempPath(), "RetroEmu-shortcut-" + Guid.NewGuid().ToString("N"));
        try
        {
            string old = Path.Combine(root, "version-vieja"); PackageTests.OwnPackage(old, "0.2.1");
            var plan = UpdatePlan.Create(old, new(new(0, 2, 2), "win-x64", "own.zip", new Uri("https://github.com/own.zip"), 1, new string('0', 64))); plan.Save();
            Directory.CreateDirectory(plan.NewRoot);
            string link = Path.Combine(root, "RetroEmu.lnk");
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            try
            {
                object shortcut = ((dynamic)shell).CreateShortcut(link);
                try { ((dynamic)shortcut).TargetPath = Path.Combine(old, "RetroEmu.exe"); ((dynamic)shortcut).WorkingDirectory = old; ((dynamic)shortcut).Arguments = "--mute"; ((dynamic)shortcut).Save(); }
                finally { Marshal.FinalReleaseComObject(shortcut); }
                byte[] original = File.ReadAllBytes(link);
                var service = new UpdateShortcut(plan, link); service.Capture(); service.PointTo(plan.NewRoot);
                shortcut = ((dynamic)shell).CreateShortcut(link);
                try
                {
                    Assert.That((string)((dynamic)shortcut).TargetPath, Is.EqualTo(Path.Combine(plan.NewRoot, "RetroEmu.exe")));
                    Assert.That((string)((dynamic)shortcut).WorkingDirectory, Is.EqualTo(plan.NewRoot));
                    Assert.That((string)((dynamic)shortcut).Arguments, Is.EqualTo("--mute"));
                }
                finally { Marshal.FinalReleaseComObject(shortcut); }
                service.Restore(); Assert.That(File.ReadAllBytes(link), Is.EqualTo(original));
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
