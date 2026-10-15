using SpaceSharp.Layout;
using SpaceSharp.Models;

namespace SpaceSharp.Tests;

public class SquarifyTests
{
    private static List<(FsNode Node, LayoutRect Rect)> Run(LayoutRect bounds, params long[] sizes)
    {
        var root = TestTree.Dir(@"C:\");
        for (int i = 0; i < sizes.Length; i++) TestTree.File(root, $"f{i}", sizes[i]);
        root.FinishDirectory();

        var result = new List<(FsNode, LayoutRect)>();
        Squarify.Layout(root.Children, bounds, SizeMeasure.FileSize, (n, r) => result.Add((n, r)));
        return result;
    }

    [Fact]
    public void SingleNodeFillsTheBounds()
    {
        var items = Run(new LayoutRect(10, 20, 300, 200), 42);
        var rect = Assert.Single(items).Rect;
        Assert.Equal(10, rect.X, 6);
        Assert.Equal(20, rect.Y, 6);
        Assert.Equal(300, rect.Width, 6);
        Assert.Equal(200, rect.Height, 6);
    }

    [Fact]
    public void AreasAreProportionalToSizes()
    {
        var bounds = new LayoutRect(0, 0, 400, 300);
        var items = Run(bounds, 6, 3, 1);

        double total = bounds.Width * bounds.Height;
        Assert.Equal(total * 0.6, Area(items[0].Rect), 6);
        Assert.Equal(total * 0.3, Area(items[1].Rect), 6);
        Assert.Equal(total * 0.1, Area(items[2].Rect), 6);
    }

    [Fact]
    public void EveryRectStaysInsideTheBounds()
    {
        var bounds = new LayoutRect(5, 5, 640, 480);
        var items = Run(bounds, 50, 30, 20, 10, 10, 5, 3, 2, 1, 1);

        Assert.Equal(10, items.Count);
        foreach (var (_, rect) in items)
        {
            Assert.True(rect.Left >= bounds.Left - 1e-6, $"{rect} left of bounds");
            Assert.True(rect.Top >= bounds.Top - 1e-6, $"{rect} above bounds");
            Assert.True(rect.Right <= bounds.Right + 1e-6, $"{rect} right of bounds");
            Assert.True(rect.Bottom <= bounds.Bottom + 1e-6, $"{rect} below bounds");
        }
    }

    [Fact]
    public void RectsDoNotOverlap()
    {
        var items = Run(new LayoutRect(0, 0, 500, 500), 40, 25, 15, 10, 5, 3, 2);

        for (int a = 0; a < items.Count; a++)
        for (int b = a + 1; b < items.Count; b++)
        {
            var overlap = LayoutRect.Intersect(items[a].Rect, items[b].Rect);
            Assert.True(overlap.IsEmpty || Area(overlap) < 1e-6, $"{items[a].Rect} overlaps {items[b].Rect}");
        }
    }

    [Fact]
    public void TotalAreaEqualsTheBounds()
    {
        var bounds = new LayoutRect(0, 0, 800, 450);
        var items = Run(bounds, 100, 80, 60, 40, 20, 10, 5, 1);
        Assert.Equal(bounds.Width * bounds.Height, items.Sum(i => Area(i.Rect)), 4);
    }

    [Fact]
    public void ZeroSizedNodesAreSkipped()
    {
        var items = Run(new LayoutRect(0, 0, 100, 100), 10, 5, 0, 0);
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.True(i.Node.Size > 0));
    }

    [Fact]
    public void EmptyBoundsEmitNothing()
    {
        Assert.Empty(Run(new LayoutRect(0, 0, 0, 100), 10, 5));
        Assert.Empty(Run(new LayoutRect(0, 0, 100, 0), 10, 5));
    }

    [Fact]
    public void NoNodesEmitNothing()
    {
        Assert.Empty(Run(new LayoutRect(0, 0, 100, 100)));
    }

    [Fact]
    public void FirstRowGoesAlongTheShorterSide()
    {
        // Wide bounds: the first row is a vertical strip that spans the full height.
        var wide = Run(new LayoutRect(0, 0, 400, 100), 50, 50);
        Assert.Equal(100, wide[0].Rect.Height, 6);

        // Tall bounds: the first row is a horizontal strip that spans the full width.
        var tall = Run(new LayoutRect(0, 0, 100, 400), 50, 50);
        Assert.Equal(100, tall[0].Rect.Width, 6);
    }

    [Fact]
    public void BiasChangesTheFirstRowsDirection()
    {
        // A square: with no bias the row goes into a vertical strip (w >= h), so its two items stack and each spans the
        // full width; a horizontal bias puts the row in a horizontal strip, so they sit side by side at full height.
        var root = TestTree.Dir(@"C:\");
        TestTree.File(root, "a", 50); TestTree.File(root, "b", 50);
        root.FinishDirectory();
        var bounds = new LayoutRect(0, 0, 200, 200);

        var plain = new List<(FsNode, LayoutRect)>();
        Squarify.Layout(root.Children, bounds, SizeMeasure.FileSize, (n, r) => plain.Add((n, r)));
        Assert.Equal(200, plain[0].Item2.Width, 6);    // stacked: full width, half height

        var wide = new List<(FsNode, LayoutRect)>();
        Squarify.Layout(root.Children, bounds, SizeMeasure.FileSize, (n, r) => wide.Add((n, r)), orientationBias: 1 / 3.0);
        Assert.Equal(200, wide[0].Item2.Height, 6);    // side by side: full height, half width

        // Areas are unchanged either way.
        Assert.Equal(20000, plain[0].Item2.Width * plain[0].Item2.Height, 4);
        Assert.Equal(20000, wide[0].Item2.Width * wide[0].Item2.Height, 4);
    }

    [Fact]
    public void UsesTheRequestedMeasure()
    {
        var root = TestTree.Dir(@"C:\");
        TestTree.File(root, "big-logical", size: 100, allocated: 10);
        TestTree.File(root, "big-on-disk", size: 10, allocated: 100);
        root.FinishDirectory();
        root.SortBy(SizeMeasure.SizeOnDisk);

        var result = new List<(FsNode Node, LayoutRect Rect)>();
        Squarify.Layout(root.Children, new LayoutRect(0, 0, 110, 100), SizeMeasure.SizeOnDisk, (n, r) => result.Add((n, r)));

        Assert.Equal("big-on-disk", result[0].Node.Name);
        Assert.Equal(10000, Area(result[0].Rect), 6);
    }

    private static double Area(LayoutRect r) => r.Width * r.Height;
}
