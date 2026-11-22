namespace Paddock.Core.Services;

/// <summary>
/// Spots a double-click on the desktop in the raw stream of button presses a
/// low-level mouse hook sees — Windows' own rule: two left presses within the
/// double-click time, the second inside the double-click rectangle around the
/// first. It knows nothing about Windows; the shell layer feeds it presses.
///
/// Only presses that land on the desktop's own window count, and a third
/// press starts a new pair (a triple-click is one double-click, not two).
/// Whether the spot is <em>bare</em> — not an icon — is for the caller to ask
/// once this says a double-click happened, which keeps the expensive question
/// off the path of every ordinary click.
/// </summary>
public sealed class DesktopDoubleClick
{
    private readonly uint _maxIntervalMs;
    private readonly int _maxDx;
    private readonly int _maxDy;

    private bool _armed;
    private uint _time;
    private int _x;
    private int _y;

    /// <param name="maxIntervalMs">The double-click time (GetDoubleClickTime).</param>
    /// <param name="maxDx">Half the double-click rectangle's width (SM_CXDOUBLECLK / 2).</param>
    /// <param name="maxDy">Half its height (SM_CYDOUBLECLK / 2).</param>
    public DesktopDoubleClick(uint maxIntervalMs, int maxDx, int maxDy)
    {
        _maxIntervalMs = maxIntervalMs;
        _maxDx = maxDx;
        _maxDy = maxDy;
    }

    /// <summary>
    /// The left button went down. <paramref name="timeMs"/> is the message
    /// clock (it wraps, which is fine); <paramref name="onDesktop"/> says it
    /// landed on the desktop rather than on some window. Returns true when this
    /// press completes a double-click there.
    /// </summary>
    public bool OnLeftButtonDown(uint timeMs, int x, int y, bool onDesktop)
    {
        if (!onDesktop)
        {
            _armed = false;
            return false;
        }

        if (_armed
            && unchecked(timeMs - _time) <= _maxIntervalMs
            && Math.Abs(x - _x) <= _maxDx
            && Math.Abs(y - _y) <= _maxDy)
        {
            _armed = false;
            return true;
        }

        _armed = true;
        _time = timeMs;
        _x = x;
        _y = y;
        return false;
    }

    /// <summary>Another button went down: whatever came before is not half a double-click any more.</summary>
    public void OnOtherButtonDown() => _armed = false;
}
