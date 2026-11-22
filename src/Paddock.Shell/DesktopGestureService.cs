using System.Runtime.InteropServices;
using Paddock.Core.Models;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Lets the user hold the right mouse button on empty desktop and drag out a
/// new paddock, Fences-style — and double-click empty desktop to hide and show
/// the paddocks — without Paddock owning any of the desktop.
///
/// A low-level mouse hook sees every mouse event and feeds them to
/// <see cref="DesktopDrawGesture"/>. The right button is swallowed only when it
/// goes down on bare desktop (Explorer's icon view, no icon under it); a press
/// that never becomes a drag is replayed as a normal right-click, so the
/// desktop's own menu still opens. Left-drag is left alone: that is the
/// desktop's rubber-band selection. Left presses are only <em>watched</em>
/// (<see cref="DesktopDoubleClick"/>), never swallowed: the cheap "is this the
/// desktop?" check happens in the hook, and the cross-process "is there an
/// icon here?" one afterwards, on this thread's message loop.
///
/// The hook lives on its own thread with its own message loop. Windows makes
/// every mouse event in the session wait for a low-level hook, so a hook on the
/// UI thread would freeze the whole mouse whenever Paddock was busy (moving
/// files, waiting on a permission prompt). Events are raised on that thread —
/// marshal to the UI yourself — and all coordinates are physical pixels.
/// </summary>
public sealed class DesktopGestureService : IDisposable
{
    /// <summary>Marks the right-click Paddock replays, so the hook lets it through.</summary>
    private static readonly IntPtr ReplayMarker = new(0x50414444);

    private const uint ReplayMessage = NativeMethods.WM_APP + 1;
    private const uint DoubleClickMessage = NativeMethods.WM_APP + 2;

    /// <summary>How often the hook is checked for having been dropped by Windows.</summary>
    private const uint HealthCheckMs = 5000;

    private readonly DesktopDrawGesture _gesture;
    private readonly DesktopDoubleClick _doubleClick;
    private readonly HookWatchdog _health = new();
    private readonly object _gate = new();

    private IntPtr _hook;
    private DateTime _lastReinstallLogged = DateTime.MinValue;

    private volatile bool _drawEnabled;
    private volatile bool _doubleClickEnabled;

    private Thread? _thread;
    private uint _threadId;
    private NativeMethods.LowLevelMouseProc? _proc;

    public DesktopGestureService()
    {
        var threshold = Math.Max(8, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDRAG) * 2);
        _gesture = new DesktopDrawGesture(threshold, minWidth: 60, minHeight: 40);

        _gesture.DrawingChanged += bounds => DrawingChanged?.Invoke(bounds);
        _gesture.Completed += bounds => DrawCompleted?.Invoke(bounds);
        _gesture.Cancelled += () => DrawCancelled?.Invoke();
        _gesture.ReplayRightClick += (_, _) =>
            NativeMethods.PostThreadMessageW(_threadId, ReplayMessage, IntPtr.Zero, IntPtr.Zero);

        _doubleClick = new DesktopDoubleClick(
            NativeMethods.GetDoubleClickTime(),
            Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDOUBLECLK) / 2),
            Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDOUBLECLK) / 2));
    }

    /// <summary>The user double-clicked empty desktop. Raised on the hook thread.</summary>
    public event Action? DesktopDoubleClicked;

    /// <summary>The rectangle being drawn (physical pixels), as it appears and changes.</summary>
    public event Action<LayoutBounds>? DrawingChanged;

    /// <summary>The user let go of a rectangle big enough for a paddock.</summary>
    public event Action<LayoutBounds>? DrawCompleted;

    /// <summary>A rectangle on screen is no longer wanted.</summary>
    public event Action? DrawCancelled;

    /// <summary>Any mouse button went down anywhere, at this physical-pixel point.</summary>
    public event Action<int, int>? ButtonPressed;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _thread is not null;
            }
        }
    }

    /// <summary>
    /// Turns the two gestures on or off. The hook runs while either is wanted
    /// and is removed when neither is.
    /// </summary>
    public void Configure(bool drawOnDesktop, bool doubleClickToHide)
    {
        _drawEnabled = drawOnDesktop;
        _doubleClickEnabled = doubleClickToHide;

        if (drawOnDesktop || doubleClickToHide)
        {
            Start();
            Log.Info($"Desktop gestures: right-drag draws a paddock {(drawOnDesktop ? "on" : "off")}, " +
                     $"double-click hides paddocks {(doubleClickToHide ? "on" : "off")}.");
        }
        else
        {
            Stop();
        }
    }

    /// <summary>Installs the hook. Safe to call when already running.</summary>
    public void Start()
    {
        using var started = new ManualResetEventSlim();

        lock (_gate)
        {
            if (_thread is not null)
                return;

            _thread = new Thread(() => Run(started))
            {
                IsBackground = true,
                Name = "Paddock desktop gestures",
            };
            _thread.Start();
        }

        started.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Removes the hook. Safe to call when not running.</summary>
    public void Stop()
    {
        Thread? thread;

        lock (_gate)
        {
            thread = _thread;
            _thread = null;
        }

        if (thread is null)
            return;

        NativeMethods.PostThreadMessageW(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose() => Stop();

    private void Run(ManualResetEventSlim started)
    {
        // Work in physical pixels throughout, like the hook's own coordinates.
        NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        _threadId = NativeMethods.GetCurrentThreadId();
        _proc = HookProc;

        // Create this thread's message queue before anyone can post to it.
        NativeMethods.PeekMessageW(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);

        _hook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandleW(null), 0);
        started.Set();

        if (_hook == IntPtr.Zero)
        {
            Log.Warn($"Could not watch the mouse for desktop gestures (error {Marshal.GetLastWin32Error()}). " +
                     "New paddocks can still be drawn, and paddocks hidden, from the tray menu.");
            lock (_gate)
            {
                _thread = null;
            }

            return;
        }

        var timer = NativeMethods.SetTimer(IntPtr.Zero, 0, HealthCheckMs, IntPtr.Zero);

        try
        {
            while (NativeMethods.GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.hwnd == IntPtr.Zero && message.message == NativeMethods.WM_TIMER)
                {
                    CheckHookHealth();
                    continue;
                }

                if (message.hwnd == IntPtr.Zero && message.message == ReplayMessage)
                {
                    ReplayRightClick();
                    continue;
                }

                if (message.hwnd == IntPtr.Zero && message.message == DoubleClickMessage)
                {
                    OnDoubleClick(new NativeMethods.POINT { X = (int)message.wParam, Y = (int)message.lParam });
                    continue;
                }

                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessageW(ref message);
            }
        }
        finally
        {
            if (timer != 0)
                NativeMethods.KillTimer(IntPtr.Zero, timer);

            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Windows silently removes a low-level hook that answers too slowly too
    /// often, and right-drag and double-click would just stop. If the pointer
    /// has moved but the hook heard nothing, install it again.
    /// </summary>
    private void CheckHookHealth()
    {
        if (!NativeMethods.GetCursorPos(out var cursor) || !_health.Check(cursor.X, cursor.Y))
            return;

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_MOUSE_LL, _proc!, NativeMethods.GetModuleHandleW(null), 0);

        // A program moving the pointer itself looks the same; say so only now and then.
        if (DateTime.UtcNow - _lastReinstallLogged > TimeSpan.FromMinutes(10))
        {
            _lastReinstallLogged = DateTime.UtcNow;
            Log.Info(_hook == IntPtr.Zero
                ? $"The desktop mouse hook went quiet and could not be installed again (error {Marshal.GetLastWin32Error()})."
                : "The desktop mouse hook went quiet (Windows may have dropped it); installed it again.");
        }
    }

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        _health.OnHookEvent();

        if (code >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

                if (info.dwExtraInfo != ReplayMarker && !IsPenOrTouch(info.dwExtraInfo) && Handle((int)wParam, info))
                    return (IntPtr)1;
            }
            catch (Exception ex)
            {
                // Nothing may escape into user32 — and a broken gesture must
                // never cost the user their mouse.
                Log.Error("Desktop gesture handling failed.", ex);
            }
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>Returns true when the event must be swallowed.</summary>
    private bool Handle(int message, NativeMethods.MSLLHOOKSTRUCT info)
    {
        var point = info.pt;

        switch (message)
        {
            case NativeMethods.WM_MOUSEMOVE:
                if (_gesture.IsActive)
                    _gesture.OnMove(point.X, point.Y);
                return false;

            case NativeMethods.WM_RBUTTONDOWN:
                ButtonPressed?.Invoke(point.X, point.Y);
                _doubleClick.OnOtherButtonDown();

                // Drawing turned off: leave the right button to Explorer, unless
                // a gesture is already under way (its release must be swallowed).
                if (!_drawEnabled && !_gesture.IsActive)
                    return false;

                return _gesture.OnRightButtonDown(
                    point.X,
                    point.Y,
                    !_gesture.IsActive && DesktopListView.IsBareDesktop(point));

            case NativeMethods.WM_RBUTTONUP:
                return _gesture.OnRightButtonUp(point.X, point.Y);

            case NativeMethods.WM_LBUTTONDOWN:
                ButtonPressed?.Invoke(point.X, point.Y);
                _gesture.OnOtherButtonDown();

                if (_doubleClickEnabled
                    && _doubleClick.OnLeftButtonDown(
                        info.time,
                        point.X,
                        point.Y,
                        DesktopListView.SurfaceAt(point) != DesktopListView.Surface.NotDesktop))
                {
                    // Whether it was on an icon needs Explorer: ask once the hook has returned.
                    NativeMethods.PostThreadMessageW(_threadId, DoubleClickMessage, point.X, point.Y);
                }

                return false;

            case NativeMethods.WM_MBUTTONDOWN:
            case NativeMethods.WM_XBUTTONDOWN:
                ButtonPressed?.Invoke(point.X, point.Y);
                _gesture.OnOtherButtonDown();
                _doubleClick.OnOtherButtonDown();
                return false;

            default:
                return false;
        }
    }

    /// <summary>A double-click landed on the desktop: act on it if no icon was under it.</summary>
    private void OnDoubleClick(NativeMethods.POINT point)
    {
        try
        {
            if (!DesktopListView.IsEmptyDesktop(point))
                return;

            Log.Info($"Double-click on empty desktop at {point.X},{point.Y}.");
            DesktopDoubleClicked?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Handling a desktop double-click failed.", ex);
        }
    }

    /// <summary>
    /// Gives Explorer the plain right-click the gesture swallowed, at the
    /// cursor, marked so the hook lets it through.
    /// </summary>
    private static void ReplayRightClick()
    {
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                mi = new NativeMethods.MOUSEINPUT { dwFlags = NativeMethods.MOUSEEVENTF_RIGHTDOWN, dwExtraInfo = ReplayMarker },
            },
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                mi = new NativeMethods.MOUSEINPUT { dwFlags = NativeMethods.MOUSEEVENTF_RIGHTUP, dwExtraInfo = ReplayMarker },
            },
        };

        if (NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) != inputs.Length)
            Log.Warn($"Could not replay a desktop right-click (error {Marshal.GetLastWin32Error()}).");
    }

    /// <summary>Pen and touch input carry this signature; leave them to Windows.</summary>
    private static bool IsPenOrTouch(IntPtr extraInfo)
        => ((long)extraInfo & 0xFFFFFF00) == 0xFF515700;
}
