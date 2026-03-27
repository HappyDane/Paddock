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
            Icon = System.Drawing.SystemIcons.Application,
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show/Hide Paddocks", null, (_, _) => TogglePaddocks());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => TogglePaddocks();
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

    protected override void OnExit(ExitEventArgs e)
    {
        PaddockManager?.SaveLayout();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
