using System.Diagnostics;
using System.Globalization;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Starts Paddock again if it crashes or freezes. While Paddock is not
/// running, the icons in paddocks are not on the desktop, so neither must
/// leave them out of sight until the next sign-in.
///
/// Each running Paddock starts one small copy of itself with
/// <see cref="Switch"/> (Paddock restarts it if it ever goes). The watchdog
/// waits for Paddock to end, and meanwhile reads its <see cref="AppHeartbeat"/>:
/// a heartbeat silent for <see cref="HangDetector.Threshold"/> means frozen,
/// and the watchdog ends it. Either way <see cref="RestartPolicy"/> decides:
/// never after a normal exit or a deliberate kill, never while Windows signs
/// out, a pause before retrying a crash at start-up, and no endless loop. When
/// it gives up, it puts every icon back on the desktop and says so.
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
    private const int CheckEveryMs = 2000;

    /// <summary>
    /// Starting up (at sign-in especially) can keep the UI thread busy for a
    /// while; freezes are only judged after this. A freeze at start-up is
    /// still caught, just later.
    /// </summary>
    private static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(60);

    /// <summary>Starts a watchdog for the current process. Never throws; null if it could not.</summary>
    public static Process? Watch(int restartsSoFar)
    {
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = $"{Switch} {Environment.ProcessId} {restartsSoFar}",
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not start the crash watchdog; Paddock will not restart itself after a crash. ({ex.Message})");
            return null;
        }
    }

    /// <summary>
    /// The watchdog's whole life: watch the Paddock given on the command line
    /// until it ends, and start it again if it crashed or froze. Returns the
    /// watchdog's own exit code.
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
        using (var heartbeat = AppHeartbeat.Open(processId))
        {
            var detector = new HangDetector();

            while (!watched.WaitForExit(CheckEveryMs))
            {
                if (heartbeat is null
                    || DateTime.Now - started < StartupGrace
                    || !detector.IsHung(Environment.TickCount64, heartbeat.LastBeat, heartbeat.WaitingOnPurpose, IsBeingDebugged(watched)))
                {
                    continue;
                }

                Log.Warn($"Paddock has not responded for {HangDetector.Threshold.TotalSeconds:0} s; ending it.");

                try
                {
                    watched.Kill();
                    watched.WaitForExit(10000);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Watchdog: could not end the frozen Paddock ({ex.Message}).");
                    return 0;
                }

                return Act(RestartPolicy.DecideAfterHang(DateTime.Now - started, restarts, IsSessionEnding()), "Paddock stopped responding");
            }

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

            if (!RestartPolicy.IsCrash(exitCode))
                return 0;

            return Act(
                RestartPolicy.Decide(exitCode, ran, restarts, IsSessionEnding()),
                $"Paddock stopped unexpectedly (exit code 0x{exitCode:X8}) after {Describe(ran)}");
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

    private static int Act(RestartDecision decision, string what)
    {
        if (decision.GaveUp)
        {
            Log.Warn($"{what}; not restarting it: {decision.Reason}.");
            GiveIconsBack();
            return 0;
        }

        if (!decision.Restart)
        {
            Log.Warn($"{what}; not restarting it: {decision.Reason}.");
            return 0;
        }

        Log.Warn($"{what}; starting it again (restart {decision.NextCount}" +
                 (decision.Delay > TimeSpan.Zero ? $", in {decision.Delay.TotalSeconds:0} s)." : ")."));

        if (decision.Delay > TimeSpan.Zero)
        {
            Thread.Sleep(decision.Delay);
            if (IsSessionEnding())
                return 0;
        }

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
            GiveIconsBack();
        }

        return 0;
    }

    /// <summary>
    /// Paddock cannot stay running: rather than leave its icons hidden in the
    /// store, put them all back on the desktop, and tell the user why.
    /// </summary>
    private static void GiveIconsBack()
    {
        var restored = 0;

        try
        {
            using var desktop = new DesktopIconService();
            restored = desktop.CreateIconStore().RestoreEverything();
            desktop.NotifyShellOfDesktopChange();
            Log.Warn($"Watchdog: put {restored} icon(s) back on the desktop, since Paddock could not stay running.");
        }
        catch (Exception ex)
        {
            Log.Error("Watchdog: could not put the icons back on the desktop.", ex);
        }

        NativeMethods.MessageBoxW(
            IntPtr.Zero,
            "Paddock kept stopping, so it is not running now." +
            (restored > 0 ? $"\n\nSo that nothing is out of sight, it put your {restored} icon(s) back on the desktop." : string.Empty) +
            $"\n\nWhat happened is in {AppPaths.LogFile}. Start Paddock again whenever you are ready.",
            "Paddock",
            NativeMethods.MB_ICONWARNING | NativeMethods.MB_SETFOREGROUND | NativeMethods.MB_TOPMOST);
    }

    private static bool IsBeingDebugged(Process process)
    {
        try
        {
            return NativeMethods.CheckRemoteDebuggerPresent(process.Handle, out var present) && present;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsSessionEnding() => NativeMethods.GetSystemMetrics(SM_SHUTTINGDOWN) != 0;

    private static string Describe(TimeSpan ran)
        => ran.TotalMinutes >= 1 ? $"{ran.TotalMinutes:0} min" : $"{ran.TotalSeconds:0} s";
}
