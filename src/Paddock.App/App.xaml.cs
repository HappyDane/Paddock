using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using Paddock.App.Services;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App;

public partial class App : Application
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private HotkeyService? _hotkeys;

    public SettingsService Settings { get; private set; } = null!;
    public PaddockManager PaddockManager { get; private set; } = null!;
    public LayoutEngine LayoutEngine { get; private set; } = null!;
    public DesktopIconService DesktopIconService { get; private set; } = null!;
    public IconStore IconStore { get; private set; } = null!;
    public IconExtractor IconExtractor { get; private set; } = null!;
    public ShellHookService ShellHookService { get; private set; } = null!;
    public StartupManager StartupManager { get; private set; } = null!;

    /// <summary>Owns the live paddock windows.</summary>
    internal PaddockHost? Host { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Services
        Settings = new SettingsService();
        DesktopIconService = new DesktopIconService();
        IconStore = DesktopIconService.CreateIconStore();
        PaddockManager = new PaddockManager(Settings, IconStore);
        LayoutEngine = new LayoutEngine();
        IconExtractor = new IconExtractor();
        ShellHookService = new ShellHookService();
        StartupManager = new StartupManager();

        ApplyTheme(Settings.Settings.GlobalTheme);

        // Sync the startup registry entry with the persisted setting
        StartupManager.SetStartupEnabled(Settings.Settings.StartWithWindows);

        // Save layout when Windows is shutting down or the user logs off
        SessionEnding += (_, _) => PaddockManager.SaveLayout();

        InitializeTrayIcon();

        Host = new PaddockHost(this);
        Host.Start();

        InitializeHotkeys();

        if (Host.PaddockCount == 0)
            ShowFirstRunHint();
    }

    /// <summary>
    /// Swaps the active theme dictionary. Called at startup and whenever the
    /// setting changes, so the choice in Settings actually takes effect.
    /// </summary>
    internal void ApplyTheme(string theme)
    {
        var path = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase)
            ? "Resources/Themes/Light.xaml"
            : "Resources/Themes/Dark.xaml";

        var dictionary = new ResourceDictionary { Source = new Uri(path, UriKind.Relative) };

        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(dictionary);
    }

    /// <summary>
    /// With no paddocks yet there is nothing on screen, so point the user at
    /// the tray menu rather than leaving them wondering whether it started.
    /// </summary>
    private void ShowFirstRunHint()
    {
        _trayIcon?.ShowBalloonTip(
            7000,
            "Paddock is running",
            "Right-click the tray icon and choose \"New Paddock...\", then drag an area on the desktop.",
            System.Windows.Forms.ToolTipIcon.Info);
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
        menu.Items.Add("New Paddock...", null, (_, _) => CreateNewPaddock());
        menu.Items.Add("Show/Hide Paddocks", null, (_, _) => TogglePaddocks());
        menu.Items.Add("-");
        menu.Items.Add("Restore All Icons to Desktop", null, (_, _) => RestoreAllIcons());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => TogglePaddocks();
    }

    /// <summary>
    /// Registers the quick-hide hotkey. Paddocks live behind other windows and
    /// pass clicks straight through to the desktop, so a keyboard shortcut (and
    /// the tray icon) is how you toggle them.
    /// </summary>
    private void InitializeHotkeys()
    {
        if (!Settings.Settings.QuickHideEnabled)
            return;

        _hotkeys = new HotkeyService();

        if (!_hotkeys.Register(Settings.Settings.QuickHideHotkey, TogglePaddocks))
        {
            System.Diagnostics.Debug.WriteLine(
                $"App: could not register quick-hide hotkey '{Settings.Settings.QuickHideHotkey}'.");
        }
    }

    private void CreateNewPaddock() => Host?.CreatePaddockInteractive();

    private void TogglePaddocks() => Host?.ToggleQuickHide();

    private void RestoreAllIcons()
    {
        if (Host is null)
            return;

        var answer = MessageBox.Show(
            "Move every item out of your paddocks and back onto the desktop?\n\n" +
            "The paddocks themselves stay where they are.",
            "Paddock",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK)
            return;

        var restored = Host.RestoreAllIconsToDesktop();

        MessageBox.Show(
            restored == 0
                ? "There was nothing to restore."
                : $"Moved {restored} item(s) back to the desktop.",
            "Paddock",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpenSettings()
    {
        var settings = new Views.SettingsWindow();
        settings.ShowDialog();
    }

    private void ExitApplication()
    {
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

        // The HICON behind this is owned by the process for its whole lifetime.
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

        _hotkeys?.Dispose();
        Host?.Dispose();
        DesktopIconService?.Dispose();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.OnExit(e);
    }
}
