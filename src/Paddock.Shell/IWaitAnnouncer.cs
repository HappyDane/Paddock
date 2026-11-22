namespace Paddock.Shell;

/// <summary>
/// Lets code that must block the UI thread on purpose — waiting for the user
/// to answer a permission prompt — say so, so the crash watchdog does not take
/// the silence for a freeze. Implemented by <see cref="AppHeartbeat"/>.
/// </summary>
public interface IWaitAnnouncer
{
    /// <summary>Until the returned scope is disposed, the UI thread is busy on purpose.</summary>
    IDisposable WaitingOnPurpose();
}
