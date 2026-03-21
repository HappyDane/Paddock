using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

public class LayoutEngineTests
{
    private readonly LayoutEngine _engine = new();

    [Fact]
    public void SnapToEdges_SnapsToLeftScreenEdge()
    {
        var paddock = new PaddockModel { X = 5, Y = 100, Width = 300, Height = 200 };

        var (x, _) = _engine.SnapToEdges(paddock, Array.Empty<PaddockModel>(), 1920, 1080);

        Assert.Equal(0, x);
    }

    [Fact]
    public void SnapToEdges_SnapsToTopScreenEdge()
    {
        var paddock = new PaddockModel { X = 100, Y = 8, Width = 300, Height = 200 };

        var (_, y) = _engine.SnapToEdges(paddock, Array.Empty<PaddockModel>(), 1920, 1080);

        Assert.Equal(0, y);
    }

    [Fact]
    public void SnapToEdges_SnapsToRightScreenEdge()
    {
        var paddock = new PaddockModel { X = 1610, Y = 100, Width = 300, Height = 200 };

        var (x, _) = _engine.SnapToEdges(paddock, Array.Empty<PaddockModel>(), 1920, 1080);

        Assert.Equal(1620, x); // 1920 - 300
    }

    [Fact]
    public void SnapToEdges_DoesNotSnapWhenFarFromEdge()
    {
        var paddock = new PaddockModel { X = 500, Y = 500, Width = 300, Height = 200 };

        var (x, y) = _engine.SnapToEdges(paddock, Array.Empty<PaddockModel>(), 1920, 1080);

        Assert.Equal(500, x);
        Assert.Equal(500, y);
    }

    [Fact]
    public void ClampToScreen_ClampsOverflowingPaddock()
    {
        var paddock = new PaddockModel { X = 1800, Y = 1000, Width = 300, Height = 200 };
        var paddocks = new List<PaddockModel> { paddock };

        _engine.ClampToScreen(paddocks, 1920, 1080);

        Assert.Equal(1620, paddock.X); // 1920 - 300
        Assert.Equal(880, paddock.Y);  // 1080 - 200
    }

    [Fact]
    public void ClampToScreen_ClampsNegativePositions()
    {
        var paddock = new PaddockModel { X = -50, Y = -30, Width = 300, Height = 200 };
        var paddocks = new List<PaddockModel> { paddock };

        _engine.ClampToScreen(paddocks, 1920, 1080);

        Assert.Equal(0, paddock.X);
        Assert.Equal(0, paddock.Y);
    }

    // --- Intelligent Spacing Tests ---

    [Fact]
    public void ApplyIntelligentSpacing_MaintainsMarginFromScreenEdges()
    {
        var paddock = new PaddockModel { X = 5, Y = 3, Width = 300, Height = 200 };
        var paddocks = new List<PaddockModel> { paddock };

        _engine.ApplyIntelligentSpacing(paddocks, 10, 1920, 1080);

        Assert.Equal(10, paddock.X);
        Assert.Equal(10, paddock.Y);
    }

    [Fact]
    public void ApplyIntelligentSpacing_MaintainsMarginFromRightAndBottomEdges()
    {
        var paddock = new PaddockModel { X = 1618, Y = 878, Width = 300, Height = 200 };
        var paddocks = new List<PaddockModel> { paddock };

        _engine.ApplyIntelligentSpacing(paddocks, 10, 1920, 1080);

        Assert.Equal(1610, paddock.X);  // 1920 - 300 - 10
        Assert.Equal(870, paddock.Y);   // 1080 - 200 - 10
    }

    [Fact]
    public void ApplyIntelligentSpacing_PushesAdjacentPaddocksApart()
    {
        var p1 = new PaddockModel { Id = "a", X = 100, Y = 100, Width = 200, Height = 200 };
        var p2 = new PaddockModel { Id = "b", X = 305, Y = 100, Width = 200, Height = 200 };
        // p2 is 5px to the right of p1 (gap = 305 - 300 = 5), but spacing = 20
        var paddocks = new List<PaddockModel> { p1, p2 };

        _engine.ApplyIntelligentSpacing(paddocks, 20, 1920, 1080);

        // p2 should be pushed to maintain 20px gap from p1
        Assert.Equal(320, p2.X); // p1.X + p1.Width + spacing = 100 + 200 + 20
    }

    [Fact]
    public void ApplyIntelligentSpacing_PushesVerticallyAdjacentPaddocksApart()
    {
        var p1 = new PaddockModel { Id = "a", X = 100, Y = 100, Width = 200, Height = 200 };
        var p2 = new PaddockModel { Id = "b", X = 100, Y = 305, Width = 200, Height = 200 };
        // p2 is 5px below p1 (gap = 305 - 300 = 5), but spacing = 20
        var paddocks = new List<PaddockModel> { p1, p2 };

        _engine.ApplyIntelligentSpacing(paddocks, 20, 1920, 1080);

        Assert.Equal(320, p2.Y); // p1.Y + p1.Height + spacing = 100 + 200 + 20
    }

    [Fact]
    public void ApplyIntelligentSpacing_DoesNotMoveLockedPaddocks()
    {
        var locked = new PaddockModel { Id = "a", X = 5, Y = 5, Width = 200, Height = 200, Locked = true };
        var paddocks = new List<PaddockModel> { locked };

        _engine.ApplyIntelligentSpacing(paddocks, 20, 1920, 1080);

        Assert.Equal(5, locked.X);
        Assert.Equal(5, locked.Y);
    }

    // --- Auto-Arrange Preset Tests ---

    [Fact]
    public void AutoArrange_TwoColumn_PositionsPaddocksCorrectly()
    {
        var p1 = new PaddockModel { Id = "a" };
        var p2 = new PaddockModel { Id = "b" };
        var paddocks = new List<PaddockModel> { p1, p2 };

        _engine.AutoArrange(paddocks, "2-column", 1000, 600, 10);

        // Column width = (1000 - 10*3) / 2 = 970/2 = 485
        // Row height = 600 - 10*2 = 580
        Assert.Equal(10, p1.X);
        Assert.Equal(10, p1.Y);
        Assert.Equal(485, p1.Width);
        Assert.Equal(580, p1.Height);

        Assert.Equal(505, p2.X); // 10 + 485 + 10
        Assert.Equal(10, p2.Y);
        Assert.Equal(485, p2.Width);
        Assert.Equal(580, p2.Height);
    }

    [Fact]
    public void AutoArrange_ThreeColumn_PositionsPaddocksCorrectly()
    {
        var p1 = new PaddockModel { Id = "a" };
        var p2 = new PaddockModel { Id = "b" };
        var p3 = new PaddockModel { Id = "c" };
        var paddocks = new List<PaddockModel> { p1, p2, p3 };

        _engine.AutoArrange(paddocks, "3-column", 1000, 600, 10);

        // Column width = (1000 - 10*4) / 3 = 960/3 = 320
        double colWidth = (1000 - 10.0 * 4) / 3;
        double height = 600 - 10.0 * 2;

        Assert.Equal(10, p1.X);
        Assert.Equal(10 + colWidth + 10, p2.X);
        Assert.Equal(10 + 2 * (colWidth + 10), p3.X);
        Assert.Equal(colWidth, p1.Width);
        Assert.Equal(height, p1.Height);
    }

    [Fact]
    public void AutoArrange_Sidebar_FirstPaddockIsNarrow()
    {
        var p1 = new PaddockModel { Id = "a" };
        var p2 = new PaddockModel { Id = "b" };
        var paddocks = new List<PaddockModel> { p1, p2 };

        _engine.AutoArrange(paddocks, "sidebar", 1200, 800, 10);

        // usableWidth = 1200 - 30 = 1170
        // sidebarWidth = 1170 / 3 = 390
        // mainWidth = 1170 - 390 = 780
        double usable = 1200 - 30;
        double sidebarWidth = usable / 3.0;
        double mainWidth = usable - sidebarWidth;

        Assert.Equal(10, p1.X);
        Assert.Equal(sidebarWidth, p1.Width);
        Assert.Equal(10 + sidebarWidth + 10, p2.X);
        Assert.Equal(mainWidth, p2.Width);
    }

    [Fact]
    public void AutoArrange_Quadrant_FourPaddocksInCorners()
    {
        var paddocks = Enumerable.Range(0, 4)
            .Select(i => new PaddockModel { Id = i.ToString() })
            .ToList();

        _engine.AutoArrange(paddocks, "quadrant", 1000, 600, 10);

        double colW = (1000 - 30) / 2.0;
        double rowH = (600 - 30) / 2.0;

        // Top-left
        Assert.Equal(10, paddocks[0].X);
        Assert.Equal(10, paddocks[0].Y);

        // Top-right
        Assert.Equal(10 + colW + 10, paddocks[1].X);
        Assert.Equal(10, paddocks[1].Y);

        // Bottom-left
        Assert.Equal(10, paddocks[2].X);
        Assert.Equal(10 + rowH + 10, paddocks[2].Y);

        // Bottom-right
        Assert.Equal(10 + colW + 10, paddocks[3].X);
        Assert.Equal(10 + rowH + 10, paddocks[3].Y);

        Assert.Equal(colW, paddocks[0].Width);
        Assert.Equal(rowH, paddocks[0].Height);
    }

    [Fact]
    public void AutoArrange_DoesNotMoveLockedPaddocks()
    {
        var locked = new PaddockModel { Id = "a", X = 50, Y = 50, Width = 100, Height = 100, Locked = true };
        var unlocked = new PaddockModel { Id = "b" };
        var paddocks = new List<PaddockModel> { locked, unlocked };

        _engine.AutoArrange(paddocks, "2-column", 1000, 600, 10);

        // Locked paddock should not move
        Assert.Equal(50, locked.X);
        Assert.Equal(50, locked.Y);
        Assert.Equal(100, locked.Width);

        // Unlocked paddock gets arranged as the only movable item
        Assert.Equal(10, unlocked.X);
        Assert.Equal(10, unlocked.Y);
    }

    [Fact]
    public void AutoArrange_ThrowsOnUnknownPreset()
    {
        var paddocks = new List<PaddockModel> { new() };

        Assert.Throws<ArgumentException>(() =>
            _engine.AutoArrange(paddocks, "unknown-preset", 1920, 1080, 10));
    }

    // --- Snap with Spacing Gap Tests ---

    [Fact]
    public void SnapToEdges_WithSpacingGap_SnapsToGapFromLeftEdge()
    {
        var paddock = new PaddockModel { X = 18, Y = 100, Width = 300, Height = 200 };

        var (x, _) = _engine.SnapToEdges(paddock, Array.Empty<PaddockModel>(), 1920, 1080, spacingGap: 15);

        Assert.Equal(15, x);
    }

    [Fact]
    public void SnapToEdges_WithSpacingGap_SnapsToGapFromRightEdge()
    {
        var paddock = new PaddockModel { X = 1600, Y = 100, Width = 300, Height = 200 };

        var (x, _) = _engine.SnapToEdges(paddock, Array.Empty<PaddockModel>(), 1920, 1080, spacingGap: 15);

        Assert.Equal(1605, x); // 1920 - 300 - 15
    }

    [Fact]
    public void SnapToEdges_WithSpacingGap_SnapsToGapFromTopEdge()
    {
        var paddock = new PaddockModel { X = 500, Y = 12, Width = 300, Height = 200 };

        var (_, y) = _engine.SnapToEdges(paddock, Array.Empty<PaddockModel>(), 1920, 1080, spacingGap: 15);

        Assert.Equal(15, y);
    }

    [Fact]
    public void SnapToEdges_WithSpacingGap_SnapsToGapFromOtherPaddock()
    {
        var other = new PaddockModel { Id = "other", X = 100, Y = 100, Width = 200, Height = 200 };
        var paddock = new PaddockModel { Id = "test", X = 312, Y = 100, Width = 300, Height = 200 };
        // paddock.X is near other.X + other.Width + spacingGap = 100 + 200 + 15 = 315

        var (x, _) = _engine.SnapToEdges(paddock, new[] { other }, 1920, 1080, spacingGap: 15);

        Assert.Equal(315, x); // snaps to other's right edge + spacing
    }

    [Fact]
    public void SnapToEdges_WithZeroSpacing_BehavesLikeOriginal()
    {
        var paddock = new PaddockModel { X = 5, Y = 100, Width = 300, Height = 200 };

        var (x, _) = _engine.SnapToEdges(paddock, Array.Empty<PaddockModel>(), 1920, 1080, spacingGap: 0);

        Assert.Equal(0, x);
    }
}
