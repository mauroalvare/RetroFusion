using System.Collections;
using System.Reflection;
using Retro.App;
using Retro.Machines;

namespace App.Tests;

[TestFixture]
public sealed class FileBrowserTests
{
    private string _folder = "";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [SetUp] public void Setup() => _folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retro-browser-" + Guid.NewGuid())).FullName;
    [TearDown] public void Cleanup() => Directory.Delete(_folder, true);
    private static object? Call(FileBrowser browser, string name, params object[] args) => typeof(FileBrowser).GetMethod(name, Private)!.Invoke(browser, args);
    private static void Set(FileBrowser browser, string name, object value) => typeof(FileBrowser).GetField(name, Private)!.SetValue(browser, value);
    private static object? Get(FileBrowser browser, string name) => typeof(FileBrowser).GetField(name, Private)!.GetValue(browser);
    private static string[] Visible(FileBrowser browser) => ((IEnumerable)Call(browser, "VisibleItems")!).Cast<object>()
        .Select(item => (string)item.GetType().GetProperty("Path")!.GetValue(item)!).ToArray();
    private string File(string name) { string path = Path.Combine(_folder, name); System.IO.File.WriteAllBytes(path, [0]); return path; }

    [Test]
    public void EveryCatalogFormatIncludingPcDisksIsVisibleWithoutShowingAllFiles()
    {
        var expected = MachineCatalog.All.SelectMany(m => m.Extensions).Concat(new[] { ".zip", ".rfstate", ".bin", ".rom" })
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(extension => File("Propio" + extension)).ToArray();
        File("Ayuda.txt"); var browser = new FileBrowser(_folder); Call(browser, "Refresh");
        Assert.That(Visible(browser), Is.EquivalentTo(expected));
    }
    [Test]
    public void SearchIsLocalAndCaseInsensitiveAndShowingAllKeepsTheFilter()
    {
        string disk = File("Disquete.IMA"), note = File("Disquete.txt"); File("Otro.tap");
        var browser = new FileBrowser(_folder); Call(browser, "Refresh"); Set(browser, "_search", "  DISQUETE ");
        Assert.That(Visible(browser), Is.EqualTo(new[] { disk }));
        Set(browser, "_showAll", true); Assert.That(Visible(browser), Is.EquivalentTo(new[] { disk, note }));
    }
    [Test]
    public void OpeningADirectoryNavigatesInsteadOfSendingItToTheEmulator()
    {
        string child = Directory.CreateDirectory(Path.Combine(_folder, "Subcarpeta")).FullName;
        string disk = Path.Combine(child, "Original.img"); System.IO.File.WriteAllBytes(disk, [0]);
        var browser = new FileBrowser(_folder); browser.Open(); Set(browser, "_selected", child);
        bool picked = false; Call(browser, "OpenSelected", (Action<string>)(_ => picked = true));
        Assert.That(picked, Is.False); Assert.That(Get(browser, "_open"), Is.EqualTo(true));
        Assert.That(Visible(browser), Is.EqualTo(new[] { disk }));
    }
    [Test]
    public void VanishedSelectionDoesNotLaunchAnotherFileOrCloseThePicker()
    {
        string path = File("Original.img"); var browser = new FileBrowser(_folder); browser.Open(); Set(browser, "_selected", path);
        System.IO.File.Delete(path); bool picked = false;
        Call(browser, "OpenSelected", (Action<string>)(_ => picked = true));
        Assert.That(picked, Is.False); Assert.That(Get(browser, "_open"), Is.EqualTo(true));
        Assert.That(Get(browser, "_problem")!.ToString(), Does.Contain("ya no está"));
    }
    [Test]
    public void FailedNavigationKeepsTheLastDirectoryAndCanRecover()
    {
        string path = File("Propio.img"); var browser = new FileBrowser(_folder); Call(browser, "Refresh");
        Assert.That(Call(browser, "Navigate", Path.Combine(_folder, "No existe")), Is.EqualTo(false));
        Assert.That(Visible(browser), Is.EqualTo(new[] { path }));
        Assert.That(Call(browser, "Navigate", _folder), Is.EqualTo(true));
        Assert.That(Get(browser, "_problem"), Is.EqualTo(""));
    }
    [Test]
    public void OpeningASelectedFileReturnsItsExactPathAndClosesThePicker()
    {
        string path = File("Disquete propio.ima"); var browser = new FileBrowser(_folder); browser.Open(); Set(browser, "_selected", path);
        string? picked = null; Call(browser, "OpenSelected", (Action<string>)(value => picked = value));
        Assert.That(picked, Is.EqualTo(path)); Assert.That(Get(browser, "_open"), Is.EqualTo(false));
    }

    [Test]
    public void RefreshUpdatesAnActiveFilterAndRemovesDeletedSelections()
    {
        string first = File("Coincide.tap");
        var browser = new FileBrowser(_folder); browser.Open();
        Set(browser, "_search", "Coincide");
        Assert.That(Visible(browser), Is.EqualTo(new[] { first }));
        string second = File("Coincide2.tap");
        Set(browser, "_selected", first); System.IO.File.Delete(first);
        Call(browser, "Refresh");
        Assert.That(Visible(browser), Is.EqualTo(new[] { second }));
        Assert.That(Get(browser, "_selected"), Is.Null);
        Set(browser, "_search", "ausente");
        Assert.That(Visible(browser), Is.Empty);
        Set(browser, "_search", "");
        Assert.That(Visible(browser), Is.EqualTo(new[] { second }));
    }
}