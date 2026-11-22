using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App;

/// <summary>
/// Paddock's entry point, and the one place that decides what this process
/// is. The jobs that need no user interface are done here, before WPF loads:
/// the crash watchdog (so it stays small, see <see cref="CrashWatchdog"/>),
/// the elevated helper that removes items from the all-users desktop, and
/// <c>--restore-all</c>. Everything else is the WPF app as usual.
/// </summary>
public static class Program
{
    /// <summary>Moves every icon out of the paddock store back onto the desktop and exits.</summary>
    public const string RestoreAllSwitch = "--restore-all";

    /// <summary>With <see cref="RestoreAllSwitch"/>: no message when done.</summary>
    public const string QuietSwitch = "--quiet";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == CrashWatchdog.Switch)
            return CrashWatchdog.Run(args);

        // Paddock relaunched itself elevated to take shortcuts off the
        // all-users desktop; this copy does its one job and leaves.
        if (args.Length == 2 && args[0] == SharedDesktopRemover.HelperSwitch)
            return SharedDesktopRemover.RunHelper(args[1]);

        if (args.Contains(RestoreAllSwitch, StringComparer.OrdinalIgnoreCase))
            return RestoreAll(quiet: args.Contains(QuietSwitch, StringComparer.OrdinalIgnoreCase));

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>
    /// The way back that needs neither a working settings.json nor a running
    /// Paddock: every item in the store goes back on the desktop. Paddocks
    /// stay, empty, and show whatever is put in them next.
    /// </summary>
    private static int RestoreAll(bool quiet)
    {
        int restored;

        try
        {
            using var desktop = new DesktopIconService();
            var store = desktop.CreateIconStore();
            restored = store.RestoreEverything();
            desktop.NotifyShellOfDesktopChange();
            Log.Info($"--restore-all: moved {restored} item(s) back to the desktop.");
        }
        catch (Exception ex)
        {
            Log.Error("--restore-all failed.", ex);

            if (!quiet)
            {
                System.Windows.MessageBox.Show(
                    $"Paddock could not put the icons back: {ex.Message}\n\nThey are safe in the hidden folder Desktop\\.Paddock.",
                    "Paddock", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }

            return 1;
        }

        if (!quiet)
        {
            System.Windows.MessageBox.Show(
                restored == 0 ? "There was nothing to put back." : $"Moved {restored} item(s) back to the desktop.",
                "Paddock", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        return 0;
    }
}
