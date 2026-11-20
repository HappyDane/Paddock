using Paddock.Core.Models;

namespace Paddock.Core.Services;

/// <summary>
/// The "hold the right button on the desktop and drag out a paddock" gesture,
/// as a plain state machine over mouse events. It knows nothing about Windows:
/// the shell layer feeds it events from a low-level mouse hook and does what the
/// return values and events say.
///
/// The one rule that matters: the right button is only taken over when it goes
/// down on <em>bare</em> desktop, and a press that never turns into a drag is
/// handed back (<see cref="ReplayRightClick"/>) so Explorer's own desktop menu
/// still opens. Everything else passes straight through.
///
/// Coordinates are whatever the caller uses (physical screen pixels for the
/// hook); the gesture only compares and subtracts them.
/// </summary>
public sealed class DesktopDrawGesture
{
    private readonly double _dragThreshold;
    private readonly double _minWidth;
    private readonly double _minHeight;

    private State _state = State.Idle;
    private double _startX;
    private double _startY;
    private LayoutBounds _current;

    public DesktopDrawGesture(double dragThreshold = 8, double minWidth = 60, double minHeight = 40)
    {
        _dragThreshold = dragThreshold;
        _minWidth = minWidth;
        _minHeight = minHeight;
    }

    private enum State
    {
        Idle,

        /// <summary>Right button down on bare desktop, not yet dragged far enough.</summary>
        Pending,

        Drawing,

        /// <summary>Cancelled while the right button is still held: swallow its release.</summary>
        Abandoned,
    }

    /// <summary>The rectangle being drawn, raised as it first appears and as it changes.</summary>
    public event Action<LayoutBounds>? DrawingChanged;

    /// <summary>The user let go of a rectangle big enough to become a paddock.</summary>
    public event Action<LayoutBounds>? Completed;

    /// <summary>A rectangle was on screen and is no longer wanted.</summary>
    public event Action? Cancelled;

    /// <summary>
    /// The right button went down and up without a drag: the caller swallowed
    /// both, so it must now give Explorer a normal right-click at this point.
    /// </summary>
    public event Action<double, double>? ReplayRightClick;

    /// <summary>True from the moment the right button is taken over until it is released.</summary>
    public bool IsActive => _state != State.Idle;

    /// <summary>
    /// Right button pressed. Returns true when the caller must swallow the event
    /// (only ever when it landed on bare desktop).
    /// </summary>
    public bool OnRightButtonDown(double x, double y, bool onBareDesktop)
    {
        if (_state != State.Idle)
            return true;

        if (!onBareDesktop)
            return false;

        _state = State.Pending;
        _startX = x;
        _startY = y;
        return true;
    }

    /// <summary>The pointer moved. Moves are never swallowed.</summary>
    public void OnMove(double x, double y)
    {
        switch (_state)
        {
            case State.Pending:
                if (Math.Abs(x - _startX) < _dragThreshold && Math.Abs(y - _startY) < _dragThreshold)
                    return;

                _state = State.Drawing;
                Update(x, y);
                return;

            case State.Drawing:
                Update(x, y);
                return;
        }
    }

    /// <summary>
    /// Right button released. Returns true when the caller must swallow it —
    /// always, if the press was taken over, so Explorer never sees half a click.
    /// </summary>
    public bool OnRightButtonUp(double x, double y)
    {
        switch (_state)
        {
            case State.Pending:
                _state = State.Idle;
                ReplayRightClick?.Invoke(x, y);
                return true;

            case State.Drawing:
                _state = State.Idle;
                Update(x, y);

                if (_current.Width >= _minWidth && _current.Height >= _minHeight)
                    Completed?.Invoke(_current);
                else
                    Cancelled?.Invoke();

                return true;

            case State.Abandoned:
                _state = State.Idle;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Any other button pressed. A gesture in progress is dropped (its right
    /// button release will still be swallowed); the event itself passes through.
    /// </summary>
    public void OnOtherButtonDown()
    {
        if (_state == State.Drawing)
            Cancelled?.Invoke();

        if (_state is State.Pending or State.Drawing)
            _state = State.Abandoned;
    }

    private void Update(double x, double y)
    {
        _current = new LayoutBounds(
            Math.Min(_startX, x),
            Math.Min(_startY, y),
            Math.Abs(x - _startX),
            Math.Abs(y - _startY));

        DrawingChanged?.Invoke(_current);
    }
}
