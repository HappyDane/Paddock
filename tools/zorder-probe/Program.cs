// Throwaway experiment: which z-order strategy keeps a window visible on the
// desktop (above wallpaper and icons) yet below application windows, on the
// Windows version this runs on? Prints a table; never fails the build.
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

static class Probe
{
    static string OutDir = ".";
    static readonly List<TestForm> Forms = new();

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0) OutDir = args[0];
        Directory.CreateDirectory(OutDir);
        try { Run(); }
        catch (Exception ex) { Say("PROBE CRASHED: " + ex); }
        return 0;
    }

    static void Run()
    {
        Say($"OS: {Environment.OSVersion}  interactive={Environment.UserInteractive}  session={Process.GetCurrentProcess().SessionId}");
        Say($"explorer processes: {Process.GetProcessesByName("explorer").Length}");
        Say($"screen: {Screen.PrimaryScreen!.Bounds}  work: {Screen.PrimaryScreen.WorkingArea}");
        Say($"desktop: {Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)}");
        Say($"shell window: {Describe(N.GetShellWindow())}  defview host: {Describe(FindDefViewHost())}");
        Say($"foreground: {Describe(N.GetForegroundWindow())}");
        foreach (var w in TopLevel())
        {
            var cls = N.ClassOf(w);
            if (N.IsWindowVisible(w) && cls is "CASCADIA_HOSTING_WINDOW_CLASS" or "ConsoleWindowClass")
            {
                N.ShowWindow(w, 6); // SW_MINIMIZE
                Say($"minimised {Describe(w)}");
            }
        }
        Pump(800);
        DumpZOrder("initial");
        Shot("0-initial");

        var strategies = new[] { "AfterHost", "Bottom", "OwnerProgman", "AboveHost" };
        var x = 40;
        foreach (var s in strategies)
        {
            var f = new TestForm(s, new Rectangle(x, 120, 200, 160));
            Forms.Add(f);
            f.Show();
            x += 230;
        }
        var repin = new System.Windows.Forms.Timer { Interval = 1000 };
        repin.Tick += (_, _) => { foreach (var f in Forms) f.Reapply(); };
        repin.Start();
        Pump(1500);
        DumpZOrder("after showing test windows");
        Report("on bare desktop");
        Shot("1-desktop");

        // A normal application window overlapping the bottom half of every test window.
        var app = new Form
        {
            Text = "Probe app window", StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(20, 200, 960, 300), BackColor = Color.Gold,
        };
        app.Show();
        app.Activate();
        Pump(800);
        Report("app window shown over lower halves");
        Shot("2-app-over");

        // Click the exposed top half of each test window, like a user would.
        foreach (var f in Forms)
        {
            var p = f.PointToScreen(new Point(100, 30));
            Click(p);
            Pump(500);
        }
        Pump(500);
        Report("after clicking each test window's exposed top half");
        DumpZOrder("after clicks");
        Shot("3-after-clicks");

        // Show desktop (Win+D).
        app.Hide();
        Pump(300);
        Keys2(N.VK_LWIN, (byte)'D');
        Pump(400);
        Report("0.4s after Win+D");
        Pump(2000);
        Report("2.4s after Win+D (re-pin timer has run)");
        DumpZOrder("after Win+D");
        Shot("4-win-d");
        Keys2(N.VK_LWIN, (byte)'D');
        Pump(1500);
        Report("after Win+D again");
        app.Show(); app.Activate();
        Pump(1500);
        Report("app window shown again after Win+D cycle");
        Shot("4b-app-again");
        app.Hide();

        // What wallpaper engines (and slideshow transitions) do: ask Progman to
        // spawn a WorkerW behind the icons.
        var progman = N.FindWindowW("Progman", null);
        N.SendMessageTimeoutW(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
        N.SendMessageTimeoutW(progman, 0x052C, (IntPtr)0xD, (IntPtr)1, 0, 1000, out _);
        Pump(1500);
        Say($"after 0x052C: defview host: {Describe(FindDefViewHost())}");
        foreach (var f in Forms) f.Reapply();
        Pump(1000);
        DumpZOrder("after 0x052C + reapply");
        Report("after 0x052C + reapply");
        Shot("5-workerw");

        foreach (var f in Forms) f.Close();
        app.Close();
    }

    static void Report(string stage)
    {
        Say($"--- {stage} ---");
        foreach (var f in Forms)
        {
            var top = Hit(f.PointToScreen(new Point(100, 30)));
            var bottom = Hit(f.PointToScreen(new Point(100, 130)));
            Say($"  {f.Strategy,-13} top: {Pixel(f, 30)} {top,-40} | bottom: {Pixel(f, 130)} {bottom}");
        }
    }

    static string Pixel(TestForm f, int y)
    {
        try
        {
            var p = f.PointToScreen(new Point(150, y));
            using var bmp = new Bitmap(1, 1);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(p, Point.Empty, new Size(1, 1));
            var c = bmp.GetPixel(0, 0);
            return c.ToArgb() == f.BackColor.ToArgb() ? "[SEEN]" : $"[hidden {c.R:X2}{c.G:X2}{c.B:X2}]";
        }
        catch (Exception ex) { return "[pixel? " + ex.Message + "]"; }
    }

    static IEnumerable<IntPtr> TopLevel()
    {
        var h = N.GetTopWindow(IntPtr.Zero);
        while (h != IntPtr.Zero) { yield return h; h = N.GetWindow(h, 2); }
    }

    static string Hit(Point p)
    {
        var h = N.WindowFromPoint(p);
        var root = N.GetAncestor(h, 2);
        var mine = Forms.FirstOrDefault(f => f.Handle == root);
        return mine is not null ? $"SELF({mine.Strategy})" : $"{Describe(root)} / {N.ClassOf(h)}";
    }

    static IntPtr FindDefViewHost()
    {
        var progman = N.GetShellWindow();
        if (N.FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return progman;
        var w = IntPtr.Zero;
        while ((w = N.FindWindowExW(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
            if (N.FindWindowExW(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return w;
        return IntPtr.Zero;
    }

    internal static string Describe(IntPtr h)
    {
        if (h == IntPtr.Zero) return "(none)";
        var title = new StringBuilder(128);
        N.GetWindowTextW(h, title, title.Capacity);
        var mine = Forms.FirstOrDefault(f => f.Handle == h);
        if (mine is not null) return $"SELF({mine.Strategy})";
        return $"{N.ClassOf(h)} \"{title}\" 0x{h.ToInt64():X}";
    }

    static void DumpZOrder(string stage)
    {
        Say($"--- z-order {stage} (top to bottom, visible only, plus shell windows) ---");
        var h = N.GetTopWindow(IntPtr.Zero);
        var i = 0;
        while (h != IntPtr.Zero && i < 400)
        {
            var cls = N.ClassOf(h);
            var visible = N.IsWindowVisible(h);
            var interesting = cls is "Progman" or "WorkerW" || Forms.Any(f => f.Handle == h);
            if (visible || interesting)
            {
                N.GetWindowRect(h, out var r);
                var ex = (long)N.GetWindowLongPtrW(h, -20);
                var owner = N.GetWindow(h, 4);
                Say($"  #{i,3} {Describe(h),-60} vis={visible} topmost={(ex & 8) != 0} rect=({r.Left},{r.Top},{r.Right},{r.Bottom}) owner={(owner == IntPtr.Zero ? "-" : N.ClassOf(owner))}");
            }
            h = N.GetWindow(h, 2);
            i++;
        }
    }

    static void Shot(string name)
    {
        try
        {
            var b = SystemInformation.VirtualScreen;
            using var bmp = new Bitmap(b.Width, b.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
            bmp.Save(Path.Combine(OutDir, name + ".png"), ImageFormat.Png);
            using var small = new Bitmap(bmp, new Size(b.Width / 2, b.Height / 2));
            using var ms = new MemoryStream();
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 55L);
            small.Save(ms, codec, ep);
            var b64 = Convert.ToBase64String(ms.ToArray());
            for (var i = 0; i < b64.Length; i += 8000)
                File.AppendAllText(Path.Combine(OutDir, "images.txt"), $"IMG {name} {i / 8000} {b64.Substring(i, Math.Min(8000, b64.Length - i))}\n");
            File.AppendAllText(Path.Combine(OutDir, "images.txt"), $"IMG {name} END\n");
        }
        catch (Exception ex) { Say($"screenshot {name} failed: {ex.Message}"); }
    }

    static void Click(Point p)
    {
        N.SetCursorPos(p.X, p.Y);
        Pump(100);
        N.mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        N.mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }

    static void Keys2(byte mod, byte key)
    {
        N.keybd_event(mod, 0, 0, IntPtr.Zero);
        N.keybd_event(key, 0, 0, IntPtr.Zero);
        N.keybd_event(key, 0, 2, IntPtr.Zero);
        N.keybd_event(mod, 0, 2, IntPtr.Zero);
    }

    internal static void Pump(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(15); }
    }

    internal static void Say(string s)
    {
        Console.WriteLine(s);
        File.AppendAllText(Path.Combine(OutDir, "probe.txt"), s + Environment.NewLine);
    }
}

sealed class TestForm : Form
{
    public readonly string Strategy;

    public TestForm(string strategy, Rectangle bounds)
    {
        Strategy = strategy;
        Text = "Probe " + strategy;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        ShowInTaskbar = false;
        BackColor = strategy switch { "AfterHost" => Color.Red, "Bottom" => Color.Lime, "OwnerProgman" => Color.Blue, _ => Color.Magenta };
        Controls.Add(new Label { Text = strategy, ForeColor = Color.White, Font = new Font("Segoe UI", 14), AutoSize = true, Location = new Point(10, 10) });
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
            if (Strategy == "OwnerProgman") cp.Parent = N.GetShellWindow(); // owner, not parent: no WS_CHILD
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Reapply();
    }

    public void Reapply()
        => N.SetWindowPos(Handle, InsertAfter(), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);

    IntPtr Host()
    {
        var progman = N.GetShellWindow();
        if (N.FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return progman;
        var w = IntPtr.Zero;
        while ((w = N.FindWindowExW(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
            if (N.FindWindowExW(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return w;
        return progman;
    }

    IntPtr InsertAfter()
    {
        switch (Strategy)
        {
            case "AfterHost": return Host();
            case "AboveHost":
            {
                var host = Host();
                var prev = N.GetWindow(host, 3); // GW_HWNDPREV
                while (prev == Handle) prev = N.GetWindow(prev, 3);
                return prev == IntPtr.Zero ? (IntPtr)0 : prev;
            }
            default: return (IntPtr)1; // HWND_BOTTOM
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0046 && m.LParam != IntPtr.Zero) // WM_WINDOWPOSCHANGING
        {
            var pos = Marshal.PtrToStructure<N.WINDOWPOS>(m.LParam);
            if ((pos.flags & 0x0004) == 0)
            {
                var after = InsertAfter();
                if (after != Handle) { pos.hwndInsertAfter = after; Marshal.StructureToPtr(pos, m.LParam, false); }
            }
        }
        base.WndProc(ref m);
    }
}

static class N
{
    public const byte VK_LWIN = 0x5B;
    [StructLayout(LayoutKind.Sequential)] public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string w);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr p, IntPtr a, string c, string w);
    [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtrW(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr e);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeoutW(IntPtr h, uint m, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r);
    public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassNameW(h, sb, sb.Capacity); return sb.ToString(); }
}
