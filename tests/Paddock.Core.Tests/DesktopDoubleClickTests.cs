using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>Double-click on the desktop to hide and show paddocks: Windows' own double-click rule.</summary>
public class DesktopDoubleClickTests
{
    private readonly DesktopDoubleClick _clicks = new(maxIntervalMs: 500, maxDx: 2, maxDy: 2);

    [Fact]
    public void TwoQuickPressesOnTheDesktop_AreADoubleClick()
    {
        Assert.False(_clicks.OnLeftButtonDown(1000, 100, 100, onDesktop: true));
        Assert.True(_clicks.OnLeftButtonDown(1300, 101, 99, onDesktop: true));
    }

    [Fact]
    public void TooSlow_IsTwoSingleClicks()
    {
        _clicks.OnLeftButtonDown(1000, 100, 100, true);
        Assert.False(_clicks.OnLeftButtonDown(1600, 100, 100, true));
    }

    [Fact]
    public void TooFarApart_IsTwoSingleClicks()
    {
        _clicks.OnLeftButtonDown(1000, 100, 100, true);
        Assert.False(_clicks.OnLeftButtonDown(1100, 104, 100, true));
    }

    [Fact]
    public void APressOffTheDesktop_BreaksThePair()
    {
        _clicks.OnLeftButtonDown(1000, 100, 100, true);
        _clicks.OnLeftButtonDown(1100, 100, 100, onDesktop: false);
        Assert.False(_clicks.OnLeftButtonDown(1200, 100, 100, true));
    }

    [Fact]
    public void AnotherButton_BreaksThePair()
    {
        _clicks.OnLeftButtonDown(1000, 100, 100, true);
        _clicks.OnOtherButtonDown();
        Assert.False(_clicks.OnLeftButtonDown(1100, 100, 100, true));
    }

    [Fact]
    public void ATripleClick_TogglesOnlyOnce()
    {
        Assert.False(_clicks.OnLeftButtonDown(1000, 100, 100, true));
        Assert.True(_clicks.OnLeftButtonDown(1100, 100, 100, true));
        Assert.False(_clicks.OnLeftButtonDown(1200, 100, 100, true));
    }

    [Fact]
    public void TheMessageClockWrapping_DoesNotMatter()
    {
        _clicks.OnLeftButtonDown(uint.MaxValue - 100, 100, 100, true);
        Assert.True(_clicks.OnLeftButtonDown(150, 100, 100, true));
    }
}
