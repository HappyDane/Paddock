using System.IO;
using System.Security.Principal;
using System.Windows.Forms;

namespace Paddock.DesktopSmoke;

internal static class Program
{
    /// <summary>Opt-in switch: this program moves files on the desktop and wipes Paddock's settings.</summary>
    private const string OptInVariable = "PADDOCK_SMOKE_TEST";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        if (args.Length == 5 && args[0] == AppWindow.Switch)
            return AppWindow.Run(args);

        if (Environment.GetEnvironmentVariable(OptInVariable) != "1")
        {
            Console.Error.WriteLine(
                $"Refusing to run: this test moves files on your desktop and deletes Paddock's settings. " +
                $"Set {OptInVariable}=1 to run it (CI does), ideally on a throwaway machine.");
            return 2;
        }

        var exe = ValueOf(args, "--exe");
        if (exe is null || !File.Exists(exe))
        {
            Console.Error.WriteLine("Usage: Paddock.DesktopSmoke --exe <path to Paddock.exe> [--out <folder>]");
            return 2;
        }

        var output = ValueOf(args, "--out") ?? "smoke-out";
        return new SmokeTest(Path.GetFullPath(exe), Path.GetFullPath(output)).Run();
    }

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    internal static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

/// <summary>
/// A plain application window, run as a separate process, used to check that
/// paddocks stay beneath applications.
/// </summary>
internal static class AppWindow
{
    internal const string Switch = "--app-window";
    internal const string Title = "Smoke App Window";

    internal static int Run(string[] args)
    {
        var form = new Form
        {
            Text = Title,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4])),
            BackColor = Color.Gold,
        };

        form.Shown += (_, _) => form.Activate();
        Application.Run(form);
        return 0;
    }
}
