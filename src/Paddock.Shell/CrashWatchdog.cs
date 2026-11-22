using System.Diagnostics;
using System.Globalization;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Starts Paddock again if it crashes. While Paddock is not running, the
/// icons in paddocks are not on the desktop, so a crash must not leave them
/// out of sight until the next sign-in.
///
/// Each running Paddock starts one small copy of itself with
/// <see cref="Switch"/>, which only waits for it to end. If it ended with a
/// crash, the watchdog starts it again (<see cref="RestartedSwitch"/>) and
/// leaves; the new Paddock starts a watchdog of its own. Whether to restart
/// is <see cref="RestartPolicy"/>: never after a normal exit or a deliberate
/// kill, never while Windows signs out, never into a crash loop.
///
/// It runs before WPF loads (see Paddock.App's Program), so it costs little.
/// </summary>
public static class CrashWatchdog
{
    /// <summary>Runs this process as the watchdog: <c>--watchdog &lt;pid&gt; &lt;restarts&gt;</c>.</summary>
    public const string Switch = "--watchdog";

    /// <summary>Paddock was started by its watchdog after a crash: <c>--restarted &lt;restarts&gt;</c>.</summary>
    public const string RestartedSwitch = "--restarted";

    private const int SM_SHUTTINGDOWN = 0x2000;

    /// <summary>Starts a watchdog for the current process. Never throws.</summary>
    public static void Watch(int restartsSoFar)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = $"{Switch} {Environment.ProcessId} {restartsSoFar}",
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not start the crash watchdog; Paddock will not restart itself after a crash. ({ex.Message})");
        }
    }

    /// <summary>
    /// The watchdog's whole life: wait for the Paddock it watches, and start
    /// it again if it crashed. Returns the watchdog's own exit code.
    /// </summary>
    public static int Run(string[] args)
    {
        if (args.Length < 3
            || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId)
            || !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var restarts))
        {
            return 2;
        }

        Process watched;
        DateTime started;

        try
        {
            // Opening it now keeps hold of its exit code, however soon it ends.
            watched = Process.GetProcessById(processId);
            started = watched.StartTime;
            _ = watched.Handle;
        }
        catch (Exception)
        {
            return 0; // Already gone, or not ours to watch.
        }

        using (watched)
        {
            watched.WaitForExit();

            int exitCode;
            TimeSpan ran;

            try
            {
                exitCode = watched.ExitCode;
                ran = watched.ExitTime - started;
            }
            catch (Exception ex)
            {
                Log.Warn($"Watchdog: could not tell how Paddock ended ({ex.Message}).");
                return 0;
            }

            var sessionEnding = NativeMethods.GetSystemMetrics(SM_SHUTTINGDOWN) != 0;
            var decision = RestartPolicy.Decide(exitCode, ran, restarts, sessionEnding);

            if (!RestartPolicy.IsCrash(exitCode))
                return 0;

            var how = $"Paddock stopped unexpectedly (exit code 0x{exitCode:X8}) after {Describe(ran)}";

            if (!decision.Restart)
            {
                Log.Warn($"{how}; not restarting it: {decision.Reason}.");
                return 0;
            }

            Log.Warn($"{how}; starting it again (restart {decision.NextCount}).");

            try
            {
                using var _ = Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    Arguments = $"{RestartedSwitch} {decision.NextCount}",
                    UseShellExecute = false,
                });
            }
            catch (Exception ex)
            {
                Log.Warn($"Watchdog: could not start Paddock again ({ex.Message}).");
            }

            return 0;
        }
    }

    /// <summary>The restart count from Paddock's own command line (0 when it was started normally).</summary>
    public static int RestartsFrom(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == RestartedSwitch && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                return count;
        }

        return 0;
    }

    private static string Describe(TimeSpan ran)
        => ran.TotalMinutes >= 1 ? $"{ran.TotalMinutes:0} min" : $"{ran.TotalSeconds:0} s";
}
