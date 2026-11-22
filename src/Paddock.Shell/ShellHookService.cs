using System.Runtime.InteropServices;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Keeps Paddock's windows at "desktop level": above the wallpaper and the
/// desktop icons, but below every ordinary application window.
///
/// The trick is <em>not</em> to reparent into the shell's WorkerW — a window
/// living there is drawn behind the desktop icons and never receives mouse
/// input. Instead each paddock stays a normal top-level window that is kept
/// <em>directly above</em> the window hosting the desktop icons (Progman, or a
/// WorkerW on some setups), wherever that window currently sits.
///
/// "Wherever it sits" matters. Normally the icon host is at the very bottom of
/// the z-order, but Show Desktop (Win+D) raises it above every application —
/// on Windows 10 / Server 2022 a full-screen WorkerW, on Windows 11 24H2 /
/// Server 2025 Progman itself. Pinning to HWND_BOTTOM, or directly
/// <em>below</em> the host, leaves paddocks (and every icon inside them)
/// hidden behind the desktop at exactly the moment the user asked to see it.
/// Both behaviours were measured on real Windows desktops; see
/// <c>tests/Paddock.DesktopSmoke</c>.
///
/// Three pieces keep this true:
/// <list type="number">
/// <item><see cref="SendToDesktopLevel"/> when the window is created, and
/// again whenever <see cref="ForegroundChanged"/> fires or a periodic re-pin
/// runs (cheap: it does nothing while the window is already in place).</item>
/// <item><see cref="EnforceDesktopZOrder"/> from the window's
/// WM_WINDOWPOSCHANGING handler, so activation and focus changes cannot lift
/// it above other windows.</item>
/// <item><see cref="StartForegroundTracking"/>, so Win+D is answered at once
/// rather than on the next periodic re-pin.</item>
/// </list>
/// </summary>
public sealed class ShellHookService : IDisposable
{
    /// <summary>The message to forward to <see cref="EnforceDesktopZOrder"/>.</summary>
    public const int WindowPosChangingMessage = NativeMethods.WM_WINDOWPOSCHANGING;

    /// <summary>
    /// Minimum gap between attempts to find the icon view again, in ms.
    /// WM_WINDOWPOSCHANGING can arrive dozens of times a second while a paddock
    /// is dragged, and when the view genuinely cannot be found (desktop icons
    /// turned off, say) an unthrottled search would run on every one of them.
    /// </summary>
    private const long ResolveThrottleMs = 1000;

    /// <summary>Guards the z-order walks against a pathological window list.</summary>
    private const int MaxWalk = 512;

    private readonly HashSet<IntPtr> _paddockWindows = new();

    private IntPtr _desktopWindow;
    private IntPtr _loggedAnchor;
    private long _lastResolveTicks;

    private IntPtr _foregroundHook;
    private NativeMethods.WinEventProc? _foregroundProc;

    /// <summary>
    /// Raised on the thread that called <see cref="StartForegroundTracking"/>
    /// whenever another window becomes the foreground window — including the
    /// desktop itself after Win+D. Re-pin paddocks in response.
    /// </summary>
    public event Action? ForegroundChanged;

    /// <summary>
    /// The top-level window that hosts the desktop icons — normally Progman,
    /// but on machines where the wallpaper is animated or slideshowed, or while
    /// Show Desktop is active on Windows 10, the icon view lives in a WorkerW.
    /// We look for the window actually containing SHELLDLL_DefView.
    ///
    /// The result is cached but re-validated on every call, which also covers
    /// Explorer restarting or switching wallpaper modes.
    /// </summary>
    public IntPtr GetDesktopWindow()
    {
        if (HostsDesktopIcons(_desktopWindow))
            return _desktopWindow;

        var now = Environment.TickCount64;
        if (_desktopWindow != IntPtr.Zero && now - _lastResolveTicks < ResolveThrottleMs)
            return _desktopWindow;

        _lastResolveTicks = now;
        _desktopWindow = ResolveDesktopWindow();

        // Worth a line in the log — but only when it actually changes: if
        // paddocks ever end up invisible, the window we chose to sit on top of
        // is the first thing to check.
        if (_desktopWindow != _loggedAnchor)
        {
            _loggedAnchor = _desktopWindow;
            Log.Info($"Desktop icon host: {NativeMethods.GetWindowClassName(_desktopWindow)} " +
                     $"(0x{_desktopWindow.ToInt64():X}).");
        }

        return _desktopWindow;
    }

    /// <summary>
    /// Marks the window as a tool window (no Alt-Tab entry, no taskbar button)
    /// and moves it into the z-order slot directly above the desktop, unless it
    /// is already there. Safe — and cheap — to call repeatedly.
    /// </summary>
    public void SendToDesktopLevel(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return;

        _paddockWindows.Add(windowHandle);

        var exStyle = (long)NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) == 0)
        {
            NativeMethods.SetWindowLongPtr(
                windowHandle,
                NativeMethods.GWL_EXSTYLE,
                (IntPtr)(exStyle | NativeMethods.WS_EX_TOOLWINDOW));
        }

        if (IsPinned(windowHandle))
            return;

        NativeMethods.SetWindowPos(
            windowHandle,
            GetInsertAfter(windowHandle),
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE
            | NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_NOOWNERZORDER);
    }

    /// <summary>Forgets a paddock window that is being closed.</summary>
    public void Release(IntPtr windowHandle) => _paddockWindows.Remove(windowHandle);

    /// <summary>
    /// Rewrites the pending WINDOWPOS so the window lands just above the
    /// desktop, whatever z-order the system was about to give it. Call this
    /// with the lParam of WM_WINDOWPOSCHANGING and let the message continue
    /// to normal processing afterwards.
    /// </summary>
    /// <returns>True when the pending position was adjusted.</returns>
    public bool EnforceDesktopZOrder(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
            return false;

        var position = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);

        // A plain move or resize leaves the z-order alone, so there is nothing
        // to correct — and forcing one on every drag step would only cause
        // needless z-order churn. Activation, by contrast, arrives with
        // SWP_NOZORDER cleared and HWND_TOP requested: that is what we override.
        if ((position.flags & NativeMethods.SWP_NOZORDER) != 0)
            return false;

        var insertAfter = GetInsertAfter(position.hwnd);
        if (position.hwndInsertAfter == insertAfter)
            return false;

        position.hwndInsertAfter = insertAfter;

        Marshal.StructureToPtr(position, lParam, fDeleteOld: false);
        return true;
    }

    /// <summary>
    /// Starts raising <see cref="ForegroundChanged"/>. Call from the UI thread:
    /// the notifications are delivered through that thread's message loop.
    /// </summary>
    public void StartForegroundTracking()
    {
        if (_foregroundHook != IntPtr.Zero)
            return;

        // Keep the delegate in a field: the hook outlives this call, and a
        // collected delegate would crash the process on the next event.
        _foregroundProc = OnForegroundEvent;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _foregroundProc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        if (_foregroundHook == IntPtr.Zero)
            Log.Warn("Could not watch foreground changes; Show Desktop is handled by the periodic re-pin only.");
    }

    public void Dispose()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        _foregroundProc = null;
        _paddockWindows.Clear();
    }

    private void OnForegroundEvent(
        IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            ForegroundChanged?.Invoke();
        }
        catch (Exception ex)
        {
            // An exception escaping into user32 would take the process down.
            Log.Error("Re-pinning after a foreground change failed.", ex);
        }
    }

    /// <summary>
    /// The window to pass as hWndInsertAfter so that <paramref name="ownWindowHandle"/>
    /// ends up directly above the icon host. SetWindowPos places a window
    /// <em>below</em> its hWndInsertAfter, so this is the window currently
    /// directly above the host (skipping the window being placed).
    /// </summary>
    private IntPtr GetInsertAfter(IntPtr ownWindowHandle)
    {
        var host = GetDesktopWindow();
        if (host == IntPtr.Zero || host == ownWindowHandle)
            return NativeMethods.HWND_BOTTOM;

        var above = NativeMethods.GetWindow(host, NativeMethods.GW_HWNDPREV);
        for (var i = 0; above == ownWindowHandle && i < MaxWalk; i++)
            above = NativeMethods.GetWindow(above, NativeMethods.GW_HWNDPREV);

        // Nothing above the host but always-on-top windows (the taskbar, say):
        // the top of the normal band is the slot just above it. Never insert
        // relative to a topmost window — that could pull a paddock into the
        // topmost band, above the user's applications.
        if (above == IntPtr.Zero || IsTopmost(above))
            return NativeMethods.HWND_TOP;

        return above;
    }

    /// <summary>
    /// True when nothing visible except other paddocks lies between the icon
    /// host and this window — i.e. it is already where we want it, and the
    /// periodic re-pin need not touch the z-order at all.
    /// </summary>
    private bool IsPinned(IntPtr windowHandle)
    {
        var host = GetDesktopWindow();
        if (host == IntPtr.Zero)
            return false;

        var current = NativeMethods.GetWindow(host, NativeMethods.GW_HWNDPREV);

        for (var i = 0; current != IntPtr.Zero && i < MaxWalk; i++)
        {
            if (current == windowHandle)
                return true;

            if (!_paddockWindows.Contains(current) && NativeMethods.IsWindowVisible(current))
                return false;

            current = NativeMethods.GetWindow(current, NativeMethods.GW_HWNDPREV);
        }

        return false;
    }

    private static bool IsTopmost(IntPtr windowHandle)
        => ((long)NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE)
            & NativeMethods.WS_EX_TOPMOST) != 0;

    private static bool HostsDesktopIcons(IntPtr windowHandle)
        => windowHandle != IntPtr.Zero
           && NativeMethods.IsWindow(windowHandle)
           && NativeMethods.FindWindowExW(windowHandle, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;

    private static IntPtr ResolveDesktopWindow()
    {
        var progman = NativeMethods.GetShellWindow();
        if (progman == IntPtr.Zero)
            progman = NativeMethods.FindWindowW("Progman", null);

        if (HostsDesktopIcons(progman))
            return progman;

        // Walk the top-level WorkerW windows looking for the one holding the
        // desktop icon view.
        var worker = IntPtr.Zero;
        while ((worker = NativeMethods.FindWindowExW(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            if (HostsDesktopIcons(worker))
                return worker;
        }

        // No icon view at all (icons hidden, or an unusual shell) — Progman is
        // still the right thing to sit on top of.
        return progman;
    }
}
