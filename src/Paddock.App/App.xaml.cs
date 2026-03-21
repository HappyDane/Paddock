using System.Windows;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App;

public partial class App : Application
{
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public SettingsService Settings { get; private set; } = null!;
    public PaddockManager PaddockManager { get; private set; } = null!;
    public DesktopIconService DesktopIconService { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize services
        Settings = new SettingsService();
        PaddockManager = new PaddockManager(Settings);
        DesktopIconService = new DesktopIconService();

        // Set up system tray icon
        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Visible = true,
            Text = "Paddock - Desktop Organizer",
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
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
