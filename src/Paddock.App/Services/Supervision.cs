using System.Diagnostics;
using System.Windows.Threading;
using Paddock.Core.Services;
using Paddock.Shell;

namespace Paddock.App.Services;

/// <summary>
/// Paddock's side of being watched over (see <see cref="CrashWatchdog"/>):
/// the heartbeat its UI thread keeps, the watchdog process it starts — and
/// starts again if it ever goes — and its word, before it closes, that it is
/// closing on purpose. One per running Paddock; start it as soon as Paddock
/// knows it is the only copy.
/// </summary>
internal sealed class Supervision : IDisposable
{
    /// <summary>How often the UI thread tells the watchdog it is alive.</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);

    /// <summary>How often Paddock checks that its watchdog is still there.</summary>
    private static readonly TimeSpan WatchdogCheckInterval = TimeSpan.FromSeconds(30);

    private readonly AppHeartbeat? _heartbeat;
    private readonly DispatcherTimer _timer;
    private Process? _watchdog;
    private DateTime _watchdogCheckedAt = DateTime.UtcNow;

    /// <param name="args">Paddock's command line: it says whether this is a restart.</param>
    internal Supervision(IReadOnlyList<string> args)
    {
        Restarts = CrashWatchdog.RestartsFrom(args);
        _heartbeat = AppHeartbeat.Start();

        // Input priority: it only beats when Paddock could also react to the user.
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = HeartbeatInterval };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _watchdog = CrashWatchdog.Watch(Restarts);
    }

    /// <summary>How many times in a row the watchdog has restarted Paddock (0 for a normal start).</summary>
    internal int Restarts { get; }

    /// <summary>For code that blocks the UI thread on purpose, so it is not taken for a freeze.</summary>
    internal IWaitAnnouncer? Waits => _heartbeat;

    /// <summary>Paddock is closing because it was asked to (Exit, signing out): the watchdog must let it go.</summary>
    internal void ExitingOnPurpose() => _heartbeat?.MarkExitingOnPurpose();

    public void Dispose()
    {
        _timer.Stop();
        _watchdog?.Dispose();
        _watchdog = null;
        _heartbeat?.Dispose();
    }

    private void Tick()
    {
        _heartbeat?.Beat();

        if (DateTime.UtcNow - _watchdogCheckedAt < WatchdogCheckInterval)
            return;

        _watchdogCheckedAt = DateTime.UtcNow;
        EnsureWatchdog();
    }

    /// <summary>The watchdog went (ended by hand, say): start another, so a crash is still caught.</summary>
    private void EnsureWatchdog()
    {
        try
        {
            if (_watchdog is { HasExited: false })
                return;

            Log.Warn("Paddock's crash watchdog was not running; starting it again.");
            _watchdog?.Dispose();
            _watchdog = CrashWatchdog.Watch(Restarts);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not check on the crash watchdog: {ex.Message}");
        }
    }
}
