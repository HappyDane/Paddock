using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using Paddock.App.Services;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App;

public partial class App : Application
{
    /// <summary>Per-user name, so Paddock still runs once per session on a shared machine.</summary>
    private const string SingleInstanceMutexName = @"Local\Paddock.SingleInstance";

    private System.Windows.Forms.NotifyIcon? _trayIcon;

    /// <summary>The heartbeat and the crash watchdog that keep Paddock running.</summary>
    private Supervision? _supervision;
    private HotkeyService? _hotkeys;
    private System.Threading.Mutex? _instanceMutex;

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

    /// <summary>The desktop mouse hook: right-drag to draw, double-click to hide.</summary>
    private DesktopGestureService? _gestures;

    /// <summary>Right-drag on the desktop to draw a paddock.</summary>
    private DesktopDrawController? _drawController;

    /// <summary>Routes new desktop items into paddocks by the user's rules.</summary>
    internal AutoOrganizeController? AutoOrganize { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The watchdog, --restore-all and the elevated helper never get here:
        // Program.Main runs those before WPF loads.
        InstallCrashHandlers();
        Log.Info($"Paddock starting (v{typeof(App).Assembly.GetName().Version}).");

        if (!ClaimSingleInstance())
        {
            // A second copy would draw a duplicate set of paddocks and fight the
            // first one over settings.json.
            Log.Info("Another instance is already running — exiting.");
            Shutdown();
            return;
        }

        // From here on, a crash or a freeze brings Paddock back: while it is
        // not running, the icons in paddocks are not on the desktop.
        _supervision = new Supervision(e.Args);
        var restarts = _supervision.Restarts;

        // Services
        Settings = new SettingsService();
        DesktopIconService = new DesktopIconService();
        IconStore = DesktopIconService.CreateIconStore();
        PaddockManager = new PaddockManager(Settings, IconStore, DesktopIconService.CreateSharedDesktopRemover(_supervision.Waits));
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

        if (restarts > 0)
        {
            Log.Info($"Paddock was restarted after a crash (restart {restarts}).");
            Notify("Paddock restarted", "Paddock stopped unexpectedly and has started again. Your paddocks are back.");
        }

        Host = new PaddockHost(this);
        Host.Start();

        _gestures = new DesktopGestureService();
        _gestures.DesktopDoubleClicked += () => Dispatcher.BeginInvoke(() => TogglePaddocks());
        _drawController = new DesktopDrawController(this, _gestures);
        ConfigureDesktopGestures();

        AutoOrganize = new AutoOrganizeController(this);
        AutoOrganize.Refresh();

        InitializeHotkeys();

        if (Host.PaddockCount == 0)
            ShowNoPaddocksHint();
    }

    /// <summary>
    /// Takes the single-instance lock, or reports that someone else holds it.
    /// </summary>
    private bool ClaimSingleInstance()
    {
        try
        {
            _instanceMutex = new System.Threading.Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirst);

            if (isFirst)
                return true;

            _instanceMutex.Dispose();
            _instanceMutex = null;
            return false;
        }
        catch (Exception ex)
        {
            // Better to run than to refuse over a lock we could not take.
            Log.Warn($"Could not check for another instance: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// A tray app has no window to show an error in, so an unhandled exception
    /// would simply make Paddock vanish. Anything that escapes a UI callback is
    /// logged and swallowed — a failed drop or menu click should never take the
    /// whole app (and the user's paddocks) down with it.
    /// </summary>
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception on the UI thread.", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                Log.Error("Unhandled exception (terminating).", exception);
            else
                Log.Error($"Unhandled non-exception fault: {args.ExceptionObject}");
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
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

        // The theme supplies colours; Controls.xaml (menus, tooltips) uses them
        // and must stay merged whichever theme is active.
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(dictionary);
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("Resources/Controls.xaml", UriKind.Relative),
        });
    }

    /// <summary>A short notice from the tray icon.</summary>
    internal void Notify(string title, string text)
        => _trayIcon?.ShowBalloonTip(7000, title, text, System.Windows.Forms.ToolTipIcon.Info);

    /// <summary>
    /// With no paddocks there is nothing on screen at all, so point the user at
    /// the tray menu rather than leaving them wondering whether it started.
    /// </summary>
    private void ShowNoPaddocksHint()
    {
        _trayIcon?.ShowBalloonTip(
            7000,
            "Paddock is running",
            Settings.Settings.DrawOnDesktop
                ? "Hold the right mouse button on an empty part of the desktop and drag out an area."
                : "Right-click the tray icon and choose \"New Paddock...\", then drag an area on the desktop.",
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
        menu.Items.Add("Auto-Organize...", null, (_, _) => Views.AutoOrganizeWindow.ShowFor(null));
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Open Icons Folder", null, (_, _) => OpenFolder(IconStore.StoreRoot));
        menu.Items.Add("Open Settings Folder", null, (_, _) => OpenFolder(AppPaths.DataFolder));
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
            Log.Warn($"Quick-hide hotkey '{Settings.Settings.QuickHideHotkey}' could not be registered.");
        }
    }

    private void CreateNewPaddock() => Host?.CreatePaddockInteractive();

    /// <summary>
    /// Applies the Draw on Desktop and Double-click Desktop settings: the
    /// mouse hook runs while either is on.
    /// </summary>
    internal void ConfigureDesktopGestures()
    {
        var settings = Settings.Settings;
        _gestures?.Configure(settings.DrawOnDesktop, settings.DoubleClickToHide);
        _drawController?.SetEnabled(settings.DrawOnDesktop);
    }

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

    /// <summary>
    /// Opens a folder in Explorer: Paddock's icon store (where paddocked icons
    /// really are) or the one holding settings.json and paddock.log.
    /// </summary>
    private static void OpenFolder(string folder)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open '{folder}'.", ex);
        }
    }

    /// <summary>
    /// Tray → Exit. While Paddock is closed, icons in paddocks are not on the
    /// desktop, so say so and offer to put them back first. (Logging off or
    /// shutting down never asks: the icons simply wait for the next start.)
    /// </summary>
    private void ExitApplication()
    {
        var moved = PaddockManager.CountMovedIcons();

        if (moved > 0)
        {
            switch (Views.ExitPromptWindow.Ask(moved, IconStore.StoreRoot))
            {
                case Views.ExitChoice.Cancel:
                    return;

                case Views.ExitChoice.RestoreToDesktop:
                    var restored = Host?.RestoreAllIconsToDesktop() ?? 0;
                    Log.Info($"Exit: put {restored} icon(s) back on the desktop first.");
                    break;

                default:
                    Log.Info($"Exit: {moved} icon(s) wait in their paddocks.");
                    break;
            }
        }

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
        // First of all: whatever happens on the way out, this is not a crash.
        _supervision?.ExitingOnPurpose();

        PaddockManager?.SaveLayout();

        _hotkeys?.Dispose();
        _gestures?.Dispose();
        _drawController?.Dispose();
        AutoOrganize?.Dispose();
        Host?.Dispose();
        ShellHookService?.Dispose();
        DesktopIconService?.Dispose();
        IconExtractor?.Dispose();

        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        Log.Info("Paddock exited.");
        _supervision?.Dispose();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.OnExit(e);
    }
}
