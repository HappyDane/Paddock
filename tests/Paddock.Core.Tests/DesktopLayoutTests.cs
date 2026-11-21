using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>Which desktop icons a newly drawn paddock takes in.</summary>
public class DesktopLayoutTests
{
    private static readonly string Desktop = Path.Combine(Path.GetTempPath(), "paddock_layout", "Desktop");
    private static readonly IconStore Store = new(Desktop, Path.Combine(Desktop, ".Paddock"));

    private static string OnDesktop(string name) => Path.Combine(Desktop, name);

    [Fact]
    public void TakesIconsCentredInsideTheArea()
    {
        var icons = new[]
        {
            new DesktopIconSpot(OnDesktop("inside.txt"), 150, 150),
            new DesktopIconSpot(OnDesktop("outside.txt"), 450, 150),
            new DesktopIconSpot(OnDesktop("below.txt"), 150, 450),
        };

        var taken = DesktopLayout.ItemsUnder(icons, new LayoutBounds(100, 100, 200, 200), Store);

        Assert.Equal([OnDesktop("inside.txt")], taken);
    }

    [Fact]
    public void AnIconOnlyOverlappingTheEdgeIsLeftAlone()
    {
        // Its centre is just outside, even though part of the icon is inside.
        var icons = new[] { new DesktopIconSpot(OnDesktop("edge.txt"), 305, 150) };

        Assert.Empty(DesktopLayout.ItemsUnder(icons, new LayoutBounds(100, 100, 200, 200), Store));
    }

    [Fact]
    public void KeepsTheDesktopsOrder()
    {
        var icons = new[]
        {
            new DesktopIconSpot(OnDesktop("b.txt"), 120, 120),
            new DesktopIconSpot(OnDesktop("a.txt"), 140, 140),
            new DesktopIconSpot(OnDesktop("c.txt"), 160, 160),
        };

        Assert.Equal(
            [OnDesktop("b.txt"), OnDesktop("a.txt"), OnDesktop("c.txt")],
            DesktopLayout.ItemsUnder(icons, new LayoutBounds(100, 100, 200, 200), Store));
    }

    [Fact]
    public void NeverTakesPaddocksOwnStoreOrAnythingInIt()
    {
        var icons = new[]
        {
            new DesktopIconSpot(Store.StoreRoot, 150, 150),
            new DesktopIconSpot(Path.Combine(Store.StoreRoot, "abc123", "file.txt"), 150, 150),
            new DesktopIconSpot(OnDesktop("real.txt"), 150, 150),
        };

        Assert.Equal([OnDesktop("real.txt")], DesktopLayout.ItemsUnder(icons, new LayoutBounds(100, 100, 200, 200), Store));
    }

    [Fact]
    public void SkipsItemsWithoutAPathAndDuplicates()
    {
        var icons = new[]
        {
            new DesktopIconSpot("", 150, 150),
            new DesktopIconSpot(OnDesktop("once.txt"), 150, 150),
            new DesktopIconSpot(OnDesktop("ONCE.txt"), 160, 160),
        };

        Assert.Single(DesktopLayout.ItemsUnder(icons, new LayoutBounds(100, 100, 200, 200), Store));
    }

    [Fact]
    public void WorksOnMonitorsLeftOfOrAboveThePrimary()
    {
        var icons = new[] { new DesktopIconSpot(OnDesktop("left.txt"), -800, -50) };

        var taken = DesktopLayout.ItemsUnder(icons, new LayoutBounds(-1000, -200, 400, 300), Store);

        Assert.Single(taken);
    }

    [Fact]
    public void NothingOnTheDesktop_NothingTaken()
        => Assert.Empty(DesktopLayout.ItemsUnder([], new LayoutBounds(0, 0, 500, 500), Store));
}
