using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Core.Tests;

/// <summary>
/// Right-drag on the desktop to draw a paddock. The gesture must never break
/// the desktop's own right-click menu, and must never leave Explorer holding
/// half a click.
/// </summary>
public class DesktopDrawGestureTests
{
    private readonly DesktopDrawGesture _gesture = new(dragThreshold: 8, minWidth: 60, minHeight: 40);
    private readonly List<LayoutBounds> _drawn = new();
    private readonly List<LayoutBounds> _completed = new();
    private readonly List<(double X, double Y)> _replayed = new();
    private int _cancelled;

    public DesktopDrawGestureTests()
    {
        _gesture.DrawingChanged += r => _drawn.Add(r);
        _gesture.Completed += r => _completed.Add(r);
        _gesture.Cancelled += () => _cancelled++;
        _gesture.ReplayRightClick += (x, y) => _replayed.Add((x, y));
    }

    [Fact]
    public void RightButtonOffTheBareDesktop_PassesThroughUntouched()
    {
        Assert.False(_gesture.OnRightButtonDown(10, 10, onBareDesktop: false));
        _gesture.OnMove(200, 200);
        Assert.False(_gesture.OnRightButtonUp(200, 200));

        Assert.False(_gesture.IsActive);
        Assert.Empty(_drawn);
        Assert.Empty(_completed);
        Assert.Empty(_replayed);
    }

    [Fact]
    public void PlainRightClickOnTheDesktop_IsHandedBackToExplorer()
    {
        Assert.True(_gesture.OnRightButtonDown(100, 100, onBareDesktop: true));
        _gesture.OnMove(103, 102); // within the drag threshold
        Assert.True(_gesture.OnRightButtonUp(103, 102));

        Assert.Equal([(103.0, 102.0)], _replayed);
        Assert.Empty(_drawn);
        Assert.Empty(_completed);
        Assert.False(_gesture.IsActive);
    }

    [Fact]
    public void RightDrag_DrawsAndCompletesARectangle()
    {
        Assert.True(_gesture.OnRightButtonDown(100, 100, onBareDesktop: true));
        _gesture.OnMove(150, 130);
        _gesture.OnMove(400, 300);
        Assert.True(_gesture.OnRightButtonUp(400, 300));

        Assert.NotEmpty(_drawn);
        Assert.Equal([new LayoutBounds(100, 100, 300, 200)], _completed);
        Assert.Empty(_replayed);
        Assert.False(_gesture.IsActive);
    }

    [Fact]
    public void DraggingUpAndLeft_StillGivesAPositiveRectangle()
    {
        _gesture.OnRightButtonDown(400, 300, onBareDesktop: true);
        _gesture.OnMove(100, 100);
        _gesture.OnRightButtonUp(100, 100);

        Assert.Equal([new LayoutBounds(100, 100, 300, 200)], _completed);
    }

    [Fact]
    public void ATooSmallRectangle_IsCancelledNotCreated()
    {
        _gesture.OnRightButtonDown(100, 100, onBareDesktop: true);
        _gesture.OnMove(130, 120);
        _gesture.OnRightButtonUp(130, 120);

        Assert.Empty(_completed);
        Assert.Equal(1, _cancelled);
        Assert.Empty(_replayed);
    }

    [Fact]
    public void AnotherButtonCancels_AndTheLaterReleaseIsStillSwallowed()
    {
        _gesture.OnRightButtonDown(100, 100, onBareDesktop: true);
        _gesture.OnMove(300, 300);

        _gesture.OnOtherButtonDown();
        Assert.Equal(1, _cancelled);

        // Explorer never saw the press, so it must not see the release either.
        Assert.True(_gesture.OnRightButtonUp(300, 300));
        Assert.Empty(_completed);
        Assert.Empty(_replayed);
        Assert.False(_gesture.IsActive);
    }

    [Fact]
    public void AnotherButtonBeforeAnyDrag_SwallowsTheReleaseWithoutReplaying()
    {
        _gesture.OnRightButtonDown(100, 100, onBareDesktop: true);
        _gesture.OnOtherButtonDown();

        Assert.True(_gesture.OnRightButtonUp(100, 100));
        Assert.Empty(_replayed);
        Assert.Equal(0, _cancelled);
    }

    [Fact]
    public void MovesWithoutAGesture_DoNothing()
    {
        _gesture.OnMove(10, 10);
        _gesture.OnMove(500, 500);

        Assert.Empty(_drawn);
        Assert.False(_gesture.IsActive);
    }

    [Fact]
    public void TheGestureCanBeRepeated()
    {
        _gesture.OnRightButtonDown(0, 0, onBareDesktop: true);
        _gesture.OnMove(100, 100);
        _gesture.OnRightButtonUp(100, 100);

        _gesture.OnRightButtonDown(200, 200, onBareDesktop: true);
        _gesture.OnMove(500, 400);
        _gesture.OnRightButtonUp(500, 400);

        Assert.Equal(
            [new LayoutBounds(0, 0, 100, 100), new LayoutBounds(200, 200, 300, 200)],
            _completed);
    }
}
