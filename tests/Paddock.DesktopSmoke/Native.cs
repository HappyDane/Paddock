using System.Runtime.InteropServices;
using System.Text;

namespace Paddock.DesktopSmoke;

/// <summary>Win32 calls the smoke test drives the desktop with.</summary>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly Point Center => new((Left + Right) / 2, (Top + Bottom) / 2);

        public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom})";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public MOUSEINPUT Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        // MOUSEINPUT is the largest member of INPUT's union, so on its own it
        // gives the struct the size SendInput expects (40 bytes on x64).
        public IntPtr ExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    internal const byte VK_CONTROL = 0x11;
    internal const byte VK_F12 = 0x7B;
    internal const byte VK_RETURN = 0x0D;
    internal const byte VK_ESCAPE = 0x1B;
    internal const byte VK_LWIN = 0x5B;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    internal const uint GW_HWNDNEXT = 2;
    internal const uint GW_HWNDPREV = 3;
    internal const uint GA_ROOT = 2;
    internal const int SW_MINIMIZE = 6;

    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>Asks a window to close, as its close button would.</summary>
    internal static void Close(IntPtr hwnd) => PostMessageW(hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint access, bool inherit, uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SuspendThread(IntPtr thread);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Freezes a process's UI thread — its first thread — the way a deadlock
    /// would, leaving its other threads running. Returns false if it could not.
    /// </summary>
    internal static bool FreezeUiThread(System.Diagnostics.Process process)
    {
        const uint THREAD_SUSPEND_RESUME = 0x0002;

        var first = process.Threads.Cast<System.Diagnostics.ProcessThread>()
            .Select(t =>
            {
                try { return (Thread: t, Started: t.StartTime); }
                catch (Exception) { return (Thread: t, Started: DateTime.MaxValue); }
            })
            .OrderBy(t => t.Started)
            .First().Thread;

        var handle = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)first.Id);
        if (handle == IntPtr.Zero)
            return false;

        try
        {
            return SuspendThread(handle) != uint.MaxValue;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Ends a process with the exit code a real crash leaves behind (an access violation).</summary>
    internal static void CrashProcess(System.Diagnostics.Process process)
        => TerminateProcess(process.Handle, 0xC0000005);

    /// <summary>
    /// Flips the desktop's View → Show desktop icons, the way Explorer's own
    /// menu does — to put the icons back if a failed step left them hidden.
    /// </summary>
    internal static void ToggleDesktopIcons(IntPtr desktopListView)
        => SendMessageTimeoutW(GetParent(desktopListView), 0x0111 /* WM_COMMAND */, 0x7402, IntPtr.Zero, 0x2 /* SMTO_ABORTIFHUNG */, 2000, out _);

    /// <summary>The colour of one screen pixel, as #RRGGBB.</summary>
    internal static string PixelAt(Point point)
    {
        using var pixel = new Bitmap(1, 1);
        using (var graphics = Graphics.FromImage(pixel))
            graphics.CopyFromScreen(point, Point.Empty, new Size(1, 1));

        var colour = pixel.GetPixel(0, 0);
        return $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    internal static long GetWindowStyle(IntPtr hwnd) => (long)GetWindowLongPtr(hwnd, -16); // GWL_STYLE

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string? className, string? title);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string path, IntPtr unused);

    internal static string TitleOf(IntPtr hwnd)
    {
        var text = new StringBuilder(256);
        GetWindowTextW(hwnd, text, text.Capacity);
        return text.ToString();
    }

    internal static string ClassOf(IntPtr hwnd)
    {
        var text = new StringBuilder(256);
        GetClassNameW(hwnd, text, text.Capacity);
        return text.ToString();
    }

    internal static uint ProcessOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    internal static List<IntPtr> TopLevelWindows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) => { windows.Add(hwnd); return true; }, IntPtr.Zero);
        return windows;
    }

    /// <summary>The top-level window the user would hit by clicking this point.</summary>
    internal static IntPtr RootWindowAt(Point point)
        => GetAncestor(WindowFromPoint(new POINT { X = point.X, Y = point.Y }), GA_ROOT);

    internal static void RefreshDesktop(string folder)
    {
        const int SHCNE_UPDATEDIR = 0x00001000;
        const uint SHCNF_PATHW = 0x0005;
        SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, folder, IntPtr.Zero);
    }

    // --- Mouse ---

    internal static void MoveTo(Point point) => Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, point);

    internal static void LeftDown(Point point) => Mouse(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, point);

    internal static void LeftUp(Point point) => Mouse(MOUSEEVENTF_LEFTUP | MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, point);

    internal static void Click(Point point)
    {
        MoveTo(point);
        Thread.Sleep(120);
        LeftDown(point);
        Thread.Sleep(60);
        LeftUp(point);
        Thread.Sleep(250);
    }

    /// <summary>Two clicks well inside the double-click time, on exactly the same spot.</summary>
    internal static void DoubleClick(Point point)
    {
        MoveTo(point);
        Thread.Sleep(150);
        LeftDown(point);
        Thread.Sleep(40);
        LeftUp(point);
        Thread.Sleep(90);
        LeftDown(point);
        Thread.Sleep(40);
        LeftUp(point);
        Thread.Sleep(300);
    }

    internal static void RightClick(Point point)
    {
        MoveTo(point);
        Thread.Sleep(120);
        Mouse(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, point);
        Thread.Sleep(60);
        Mouse(MOUSEEVENTF_RIGHTUP | MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, point);
        Thread.Sleep(400);
    }

    /// <summary>
    /// A human-paced drag: press, nudge past the drag threshold, glide to the
    /// target in small steps (OLE drag-and-drop tracks real mouse movement),
    /// linger so the target sees DragOver, release.
    /// </summary>
    internal static void Drag(Point from, Point to, bool rightButton = false)
    {
        var down = rightButton ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
        var up = rightButton ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;
        const uint at = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

        MoveTo(from);
        Thread.Sleep(200);
        Mouse(down | at, from);
        Thread.Sleep(200);

        for (var i = 1; i <= 4; i++)
        {
            MoveTo(new Point(from.X + i * 3, from.Y + i * 3));
            Thread.Sleep(40);
        }

        const int steps = 30;
        for (var i = 1; i <= steps; i++)
        {
            var x = from.X + (to.X - from.X) * i / steps;
            var y = from.Y + (to.Y - from.Y) * i / steps;
            MoveTo(new Point(x, y));
            Thread.Sleep(30);
        }

        for (var i = 0; i < 6; i++)
        {
            MoveTo(new Point(to.X + (i % 2), to.Y));
            Thread.Sleep(80);
        }

        Mouse(up | at, to);
        Thread.Sleep(500);
    }

    private static void Mouse(uint flags, Point point)
    {
        // Absolute coordinates are normalised to 0..65535 across the virtual desktop.
        var left = GetSystemMetrics(76);   // SM_XVIRTUALSCREEN
        var top = GetSystemMetrics(77);    // SM_YVIRTUALSCREEN
        var width = GetSystemMetrics(78);  // SM_CXVIRTUALSCREEN
        var height = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN

        var input = new INPUT
        {
            Type = INPUT_MOUSE,
            Mouse = new MOUSEINPUT
            {
                Dx = (int)Math.Round((point.X - left) * 65535.0 / Math.Max(1, width - 1)),
                Dy = (int)Math.Round((point.Y - top) * 65535.0 / Math.Max(1, height - 1)),
                Flags = flags,
            },
        };

        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    // --- Keyboard ---

    internal static void KeyDown(byte vk) => keybd_event(vk, 0, 0, IntPtr.Zero);

    internal static void KeyUp(byte vk) => keybd_event(vk, 0, KEYEVENTF_KEYUP, IntPtr.Zero);

    internal static void Press(byte key)
    {
        KeyDown(key);
        Thread.Sleep(30);
        KeyUp(key);
        Thread.Sleep(30);
    }

    internal static void Chord(byte modifier, byte key)
    {
        KeyDown(modifier);
        KeyDown(key);
        Thread.Sleep(30);
        KeyUp(key);
        KeyUp(modifier);
    }
}
