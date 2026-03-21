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
}
