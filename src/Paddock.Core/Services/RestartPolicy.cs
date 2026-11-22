namespace Paddock.Core.Services;

/// <summary>What the watchdog decided about a Paddock process that ended.</summary>
/// <param name="Restart">Start Paddock again.</param>
/// <param name="NextCount">The restart count to hand the new process (it keeps counting quick restarts).</param>
/// <param name="Reason">Why, for the log.</param>
public readonly record struct RestartDecision(bool Restart, int NextCount, string Reason);

/// <summary>
/// When Paddock should be started again after it ends. While it is not
/// running, the icons in paddocks are not on the desktop, so a crash must not
/// leave them out of sight until the next sign-in — but a deliberate exit or
/// kill must be respected, and a crash loop must not be fed.
/// </summary>
public static class RestartPolicy
{
    /// <summary>A crash sooner than this after starting will only happen again.</summary>
    public static readonly TimeSpan MinimumUptime = TimeSpan.FromSeconds(10);

    /// <summary>Running this long counts as recovered: the restart count starts again.</summary>
    public static readonly TimeSpan StableUptime = TimeSpan.FromMinutes(10);

    /// <summary>Restarts allowed in a row without a stable run in between.</summary>
    public const int MaxQuickRestarts = 3;

    /// <summary>
    /// True for the exit codes of a crash: NTSTATUS failures (0xC0000005 and
    /// friends), the runtime's unhandled-exception and fail-fast codes — all
    /// with the top bit set. A normal exit is 0; Task Manager and taskkill end
    /// a process with 1, and .NET's Process.Kill (and stopping a debugger)
    /// with -1: those were meant.
    /// </summary>
    public static bool IsCrash(int exitCode) => exitCode < 0 && exitCode != -1;

    public static RestartDecision Decide(int exitCode, TimeSpan ran, int restartsSoFar, bool sessionEnding)
    {
        if (!IsCrash(exitCode))
            return new RestartDecision(false, 0, "it was closed or ended on purpose");

        if (sessionEnding)
            return new RestartDecision(false, 0, "Windows is signing out or shutting down");

        if (ran < MinimumUptime)
            return new RestartDecision(false, restartsSoFar, "it crashed right after starting, so it would only crash again");

        var count = ran >= StableUptime ? 0 : restartsSoFar;
        if (count >= MaxQuickRestarts)
            return new RestartDecision(false, count, $"it has crashed {count + 1} times in a row");

        return new RestartDecision(true, count + 1, "it crashed");
    }
}
