namespace Paddock.Core.Services;

/// <summary>
/// Tells a frozen Paddock from a busy or paused one, from the heartbeat its
/// UI thread writes every couple of seconds. The watchdog checks it often;
/// only a heartbeat that has stopped for <see cref="Threshold"/> counts, and
/// never while Paddock has said it is waiting on purpose (for the answer to a
/// permission prompt), while a debugger holds it, or before it has had
/// <see cref="Threshold"/> to beat again after the PC slept.
/// Times are milliseconds on a clock both processes share (GetTickCount64).
/// </summary>
public sealed class HangDetector
{
    /// <summary>How long without a heartbeat means frozen.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Checks further apart than this mean the watchdog itself was not
    /// running — the PC slept, or was too busy to schedule it — so the silence
    /// says nothing about Paddock.
    /// </summary>
    private static readonly TimeSpan MaxCheckGap = TimeSpan.FromSeconds(15);

    private long? _lastCheck;
    private long _quietSince = long.MinValue;

    /// <summary>
    /// Returns true when Paddock should be considered frozen. <paramref name="lastBeat"/>
    /// is when its UI thread last wrote a heartbeat.
    /// </summary>
    public bool IsHung(long now, long lastBeat, bool busy, bool debugged)
    {
        if (_lastCheck is { } previous && now - previous > MaxCheckGap.TotalMilliseconds)
            _quietSince = now; // just woke up: give it a fresh chance

        _lastCheck = now;

        if (busy || debugged)
        {
            _quietSince = now;
            return false;
        }

        var since = Math.Max(lastBeat, _quietSince);
        return now - since > Threshold.TotalMilliseconds;
    }
}
