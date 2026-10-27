using System.Numerics;
using Retro.App;

namespace App.Tests;

public sealed class InterfaceLayoutTests
{
    private static readonly DebuggerLayout.Panel[] Panels = Enum.GetValues<DebuggerLayout.Panel>();

    [TestCase(1280, 760, true)] [TestCase(1280, 760, false)] [TestCase(640, 400, true)] [TestCase(2560, 1380, false)]
    public void DebuggerPanelsFitTheAreaWithoutOverlapping(float width, float height, bool tape)
    {
        var origin = new Vector2(4, 32);
        var area = new Vector2(width, height);
        var shown = Panels.Where(panel => tape || panel != DebuggerLayout.Panel.Tape).ToList();
        var rectangles = shown.Select(panel => DebuggerLayout.Place(panel, origin, area, tape)).ToList();
        foreach (var (position, size) in rectangles)
        {
            Assert.That(position.X, Is.GreaterThanOrEqualTo(origin.X));
            Assert.That(position.Y, Is.GreaterThanOrEqualTo(origin.Y));
            Assert.That(position.X + size.X, Is.LessThanOrEqualTo(origin.X + area.X));
            Assert.That(position.Y + size.Y, Is.LessThanOrEqualTo(origin.Y + area.Y));
            Assert.That(size.X, Is.GreaterThan(area.X * 0.2f));
        }
        for (int a = 0; a < rectangles.Count; a++)
            for (int b = a + 1; b < rectangles.Count; b++)
                Assert.That(Overlap(rectangles[a], rectangles[b]), Is.False, $"{shown[a]} y {shown[b]} se superponen.");
        float covered = rectangles.Sum(r => (r.Size.X + DebuggerLayout.Gap) * (r.Size.Y + DebuggerLayout.Gap));
        Assert.That(covered, Is.EqualTo(area.X * area.Y).Within(1), "Sin cinta, la pila ocupa su lugar.");
    }

    [Test]
    public void TheTapeSharesTheStackColumnOnlyOnTheSpectrum()
    {
        var area = new Vector2(1200, 800);
        var withTape = DebuggerLayout.Place(DebuggerLayout.Panel.Stack, Vector2.Zero, area, tape: true);
        var withoutTape = DebuggerLayout.Place(DebuggerLayout.Panel.Stack, Vector2.Zero, area, tape: false);
        var tape = DebuggerLayout.Place(DebuggerLayout.Panel.Tape, Vector2.Zero, area, tape: true);
        Assert.That(withTape.Size.Y, Is.LessThan(withoutTape.Size.Y));
        Assert.That(tape.Position.X, Is.EqualTo(withTape.Position.X));
        Assert.That(tape.Position.Y, Is.EqualTo(withTape.Position.Y + withTape.Size.Y + DebuggerLayout.Gap));
    }

    [Test]
    public void FrameCacheRecomputesOnlyWhenItsKeyChanges()
    {
        var cache = new FrameCache<(int Revision, string Search), int>();
        int calls = 0;
        int Compute() => ++calls;
        for (int frame = 0; frame < 120; frame++) Assert.That(cache.Get((1, ""), Compute), Is.EqualTo(1));
        Assert.That(cache.Get((1, "a"), Compute), Is.EqualTo(2));
        Assert.That(cache.Get((2, "a"), Compute), Is.EqualTo(3));
        Assert.That(cache.Get((2, "a"), Compute), Is.EqualTo(3));
        cache.Invalidate();
        Assert.That(cache.Get((2, "a"), Compute), Is.EqualTo(4));
        Assert.That(cache.Computations, Is.EqualTo(4));
    }

    private static bool Overlap((Vector2 Position, Vector2 Size) a, (Vector2 Position, Vector2 Size) b) =>
        a.Position.X < b.Position.X + b.Size.X && b.Position.X < a.Position.X + a.Size.X &&
        a.Position.Y < b.Position.Y + b.Size.Y && b.Position.Y < a.Position.Y + a.Size.Y;
}
