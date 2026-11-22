namespace Paddock.Core.Services;

/// <summary>
/// Notices when Windows has quietly dropped a low-level mouse hook. Windows
/// removes such a hook, without telling its owner, if it is too slow to answer
/// too often; right-drag and double-click on the desktop would then simply
/// stop working. The sign is the pointer moving while the hook hears nothing.
/// </summary>
public sealed class HookWatchdog
{
    private int _events;
    private (int X, int Y)? _last;

    /// <summary>The hook was called (any mouse event).</summary>
    public void OnHookEvent() => _events++;

    /// <summary>
    /// A periodic check with the pointer's current position. Returns true when
    /// it moved since the last check but the hook saw no event at all — time
    /// to install it again. (A program moving the pointer itself also looks
    /// like this; reinstalling is harmless.)
    /// </summary>
    public bool Check(int x, int y)
    {
        var moved = _last is { } last && (last.X != x || last.Y != y);
        var silent = _events == 0;

        _last = (x, y);
        _events = 0;

        return moved && silent;
    }
}
