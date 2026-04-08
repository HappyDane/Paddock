using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App;

public partial class App : Application
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public SettingsService Settings { get; private set; } = null!;
    public PaddockManager PaddockManager { get; private set; } = null!;
    public LayoutEngine LayoutEngine { get; private set; } = null!;
    public DesktopIconService DesktopIconService { get; private set; } = null!;
    public IconExtractor IconExtractor { get; private set; } = null!;
    public ShellHookService ShellHookService { get; private set; } = null!;
    public StartupManager StartupManager { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize services
        Settings = new SettingsService();
        PaddockManager = new PaddockManager(Settings);
        LayoutEngine = new LayoutEngine();
        DesktopIconService = new DesktopIconService();
        IconExtractor = new IconExtractor();
        ShellHookService = new ShellHookService();
        StartupManager = new StartupManager();

        // Sync the startup registry entry with the persisted setting
        StartupManager.SetStartupEnabled(Settings.Settings.StartWithWindows);

        // Save layout when Windows is shutting down or the user logs off
        SessionEnding += (_, _) => PaddockManager.SaveLayout();

        // Set up system tray icon
        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Visible = true,
            Text = "Paddock - Desktop Organizer",
            Icon = CreateTrayIcon(),
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("New Paddock", null, (_, _) => CreateNewPaddock());
        menu.Items.Add("Show/Hide Paddocks", null, (_, _) => TogglePaddocks());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => TogglePaddocks();
    }

    private void CreateNewPaddock()
    {
        if (MainWindow is Views.DesktopOverlay overlay)
        {
            // Make sure the overlay is visible so the new paddock is actually shown.
            if (!overlay.IsVisible)
                overlay.Visibility = Visibility.Visible;

            overlay.CreatePaddockAtScreenCenter();
        }
    }

    private void TogglePaddocks()
    {
        if (MainWindow is not null)
        {
            MainWindow.Visibility = MainWindow.IsVisible
                ? Visibility.Hidden
                : Visibility.Visible;
        }
    }

    private void OpenSettings()
    {
        var settings = new Views.SettingsWindow();
        settings.ShowDialog();
    }

    private void ExitApplication()
    {
        _trayIcon?.Dispose();
        PaddockManager.SaveLayout();
        Shutdown();
    }

    /// <summary>
    /// Generates a 32x32 tray icon at runtime — a stylised fence/paddock glyph
    /// so the app is recognisable in the system tray without shipping a separate .ico.
    /// </summary>
    private static Icon CreateTrayIcon()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Rounded-rect background (#0A84FF accent blue)
        using var bgBrush = new SolidBrush(Color.FromArgb(10, 132, 255));
        using var bgPath = RoundedRect(new Rectangle(0, 0, size, size), 7);
        g.FillPath(bgBrush, bgPath);

        // Draw three vertical fence posts
        using var postPen = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int[] postXs = [9, 16, 23];
        foreach (int px in postXs)
        {
            g.DrawLine(postPen, px, 8, px, 24);
        }

        // Draw two horizontal rails
        using var railPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(railPen, 6, 13, 26, 13);
        g.DrawLine(railPen, 6, 19, 26, 19);

        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PaddockManager?.SaveLayout();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
