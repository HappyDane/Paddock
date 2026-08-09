using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>
/// Covers the monitor-aware overloads: paddock coordinates are screen
/// coordinates, so snapping and clamping have to work against a region whose
/// origin is not 0,0 (a second monitor left of or above the primary one).
/// </summary>
public class LayoutEngineBoundsTests
{
    private readonly LayoutEngine _engine = new();

    private static PaddockModel Paddock(double x, double y, double width = 300, double height = 200)
        => new() { Id = "p", X = x, Y = y, Width = width, Height = height };

    [Fact]
    public void ClampToBounds_KeepsPaddockInsideAnOffsetMonitor()
    {
        // A monitor to the left of the primary one.
        var bounds = new LayoutBounds(-1920, 0, 1920, 1080);
        var paddock = Paddock(-2500, 1200);

        _engine.ClampToBounds(paddock, bounds);

        Assert.Equal(-1920, paddock.X);
        Assert.Equal(880, paddock.Y);
    }

    [Fact]
    public void ClampToBounds_PullsAnOverflowingPaddockBackInside()
    {
        var bounds = new LayoutBounds(0, 0, 1920, 1080);
        var paddock = Paddock(1800, 1000);

        _engine.ClampToBounds(paddock, bounds);

        Assert.Equal(1620, paddock.X);
        Assert.Equal(880, paddock.Y);
    }

    [Fact]
    public void ClampToBounds_RaisesAZeroSizedPaddockToSomethingUsable()
    {
        var paddock = Paddock(10, 10, width: 0, height: 0);

        _engine.ClampToBounds(paddock, new LayoutBounds(0, 0, 1920, 1080));

        Assert.Equal(LayoutEngine.MinPaddockWidth, paddock.Width);
        Assert.Equal(LayoutEngine.MinPaddockHeight, paddock.Height);
    }

    [Fact]
    public void ClampToBounds_ShrinksAPaddockLargerThanItsMonitor()
    {
        var paddock = Paddock(0, 0, width: 3000, height: 2000);

        _engine.ClampToBounds(paddock, new LayoutBounds(0, 0, 1920, 1080));

        Assert.Equal(1920, paddock.Width);
        Assert.Equal(1080, paddock.Height);
    }

    [Fact]
    public void SnapToEdges_SnapsToTheEdgesOfAnOffsetMonitor()
    {
        var bounds = new LayoutBounds(-1920, 0, 1920, 1080);
        // Nearly flush with that monitor's left edge.
        var paddock = Paddock(-1912, 500);

        var (x, _) = _engine.SnapToEdges(paddock, [paddock], bounds);

        Assert.Equal(-1920, x);
    }

    [Fact]
    public void SnapToEdges_RespectsTheSpacingGapFromAnOffsetEdge()
    {
        var bounds = new LayoutBounds(-1920, 0, 1920, 1080);
        var paddock = Paddock(-1900, 500);

        var (x, _) = _engine.SnapToEdges(paddock, [paddock], bounds, spacingGap: 12);

        Assert.Equal(-1908, x);
    }

    [Fact]
    public void SnapToEdges_SnapsToTheFarEdgeOfAnOffsetMonitor()
    {
        var bounds = new LayoutBounds(-1920, 0, 1920, 1080);
        // Right edge would land at -8; the monitor's right edge is 0.
        var paddock = Paddock(-308, 500);

        var (x, _) = _engine.SnapToEdges(paddock, [paddock], bounds);

        Assert.Equal(-300, x);
    }

    [Fact]
    public void SnapToEdges_LeavesAPaddockAloneWhenNothingIsNearby()
    {
        var bounds = new LayoutBounds(0, 0, 1920, 1080);
        var paddock = Paddock(600, 400);

        var (x, y) = _engine.SnapToEdges(paddock, [paddock], bounds);

        Assert.Equal(600, x);
        Assert.Equal(400, y);
    }

    [Fact]
    public void LayoutBounds_ContainsExcludesTheFarEdges()
    {
        var bounds = new LayoutBounds(-100, -50, 200, 100);

        Assert.True(bounds.Contains(-100, -50));
        Assert.True(bounds.Contains(0, 0));
        Assert.False(bounds.Contains(100, 0));
        Assert.False(bounds.Contains(0, 50));
    }
}
