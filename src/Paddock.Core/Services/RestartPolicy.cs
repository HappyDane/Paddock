namespace Paddock.Core.Services;

/// <summary>What the watchdog decided about a Paddock process that ended (or froze).</summary>
/// <param name="Restart">Start Paddock again.</param>
/// <param name="NextCount">The restart count to hand the new process (it keeps counting quick restarts).</param>
/// <param name="Delay">How long to wait before starting it.</param>
/// <param name="Reason">Why, for the log.</param>
public readonly record struct RestartDecision(bool Restart, int NextCount, TimeSpan Delay, string Reason)
{
    /// <summary>Not restarting because restarts kept failing — the icons should go back on the desktop.</summary>
    public bool GaveUp { get; init; }
}

/// <summary>
/// When Paddock should be started again. While it is not running, the icons
/// in paddocks are not on the desktop, so a crash or a freeze must not leave
/// them out of sight until the next sign-in — but a deliberate exit or kill
/// must be respected, and a crash loop must not be fed for ever.
/// </summary>
public static class RestartPolicy
{
    /// <summary>A crash sooner than this after starting is retried only after a pause.</summary>
    public static readonly TimeSpan MinimumUptime = TimeSpan.FromSeconds(10);

    /// <summary>Running this long counts as recovered: the restart count starts again.</summary>
    public static readonly TimeSpan StableUptime = TimeSpan.FromMinutes(10);

    /// <summary>Restarts allowed in a row without a stable run in between.</summary>
    public const int MaxQuickRestarts = 3;

    /// <summary>
    /// Pauses before retrying a crash that came right after starting — at
    /// sign-in, say, before the desktop is ready. Growing, so a lasting fault
    /// is not hammered.
    /// </summary>
    private static readonly TimeSpan[] StartupBackoff =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    /// <summary>
    /// True for the exit codes of a crash: NTSTATUS failures (0xC0000005 and
    /// friends), the runtime's unhandled-exception and fail-fast codes — all
    /// with the top bit set. A normal exit is 0; Task Manager and taskkill end
    /// a process with 1, and .NET's Process.Kill (and stopping a debugger)
    /// with -1: those were meant.
    /// </summary>
    public static bool IsCrash(int exitCode) => exitCode < 0 && exitCode != -1;

    /// <summary>Paddock ended with this exit code after running this long.</summary>
    public static RestartDecision Decide(int exitCode, TimeSpan ran, int restartsSoFar, bool sessionEnding)
        => IsCrash(exitCode)
            ? DecideRestart(ran, restartsSoFar, sessionEnding, "it crashed")
            : new RestartDecision(false, 0, TimeSpan.Zero, "it was closed or ended on purpose");

    /// <summary>Paddock stopped responding and the watchdog ended it.</summary>
    public static RestartDecision DecideAfterHang(TimeSpan ran, int restartsSoFar, bool sessionEnding)
        => DecideRestart(ran, restartsSoFar, sessionEnding, "it stopped responding");

    private static RestartDecision DecideRestart(TimeSpan ran, int restartsSoFar, bool sessionEnding, string what)
    {
        if (sessionEnding)
            return new RestartDecision(false, 0, TimeSpan.Zero, "Windows is signing out or shutting down");

        var count = ran >= StableUptime ? 0 : restartsSoFar;
        if (count >= MaxQuickRestarts)
        {
            return new RestartDecision(false, count, TimeSpan.Zero, $"{what}, and has needed {count} restarts in a row already")
            {
                GaveUp = true,
            };
        }

        var delay = ran < MinimumUptime ? StartupBackoff[Math.Min(count, StartupBackoff.Length - 1)] : TimeSpan.Zero;
        return new RestartDecision(true, count + 1, delay, what);
    }
}
