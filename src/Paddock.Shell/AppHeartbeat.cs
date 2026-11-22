using System.IO.MemoryMappedFiles;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// How the crash watchdog tells a frozen Paddock from a working one. Paddock's
/// UI thread writes the time into a few bytes of shared memory every couple of
/// seconds (<see cref="Beat"/>); the watchdog reads them
/// (<see cref="Open"/>). Around a wait that blocks the UI thread on purpose —
/// the answer to a permission prompt — Paddock says so
/// (<see cref="WaitingOnPurpose"/>), and the silence does not count.
/// Times come from GetTickCount64, a clock every process shares.
/// </summary>
public sealed class AppHeartbeat : IDisposable
{
    private const int BeatOffset = 0;
    private const int WaitingOffset = 8;
    private const int Size = 16;

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private int _waiting;

    private AppHeartbeat(MemoryMappedFile file)
    {
        _file = file;
        _view = file.CreateViewAccessor(0, Size);
        Beat();
    }

    /// <summary>This process's heartbeat, once <see cref="Start"/> has run.</summary>
    public static AppHeartbeat? Current { get; private set; }

    private static string NameFor(int processId) => $@"Local\Paddock.Heartbeat.{processId}";

    /// <summary>Creates this process's heartbeat. Without one, only crashes are watched for.</summary>
    public static AppHeartbeat? Start()
    {
        try
        {
            Current = new AppHeartbeat(MemoryMappedFile.CreateNew(NameFor(Environment.ProcessId), Size));
            return Current;
        }
        catch (Exception ex)
        {
            Log.Warn($"No heartbeat for the watchdog, so a frozen Paddock will not be restarted ({ex.Message}).");
            return null;
        }
    }

    /// <summary>"Still alive." Call from the UI thread, every couple of seconds.</summary>
    public void Beat() => _view.Write(BeatOffset, Environment.TickCount64);

    /// <summary>
    /// The UI thread is about to block on purpose (waiting for the user to
    /// answer a permission prompt). Until the scope is disposed, the watchdog
    /// does not take the silence for a freeze.
    /// </summary>
    public IDisposable WaitingOnPurpose()
    {
        _view.Write(WaitingOffset, (long)Interlocked.Increment(ref _waiting));
        return new WaitScope(this);
    }

    public void Dispose()
    {
        _view.Dispose();
        _file.Dispose();

        if (ReferenceEquals(Current, this))
            Current = null;
    }

    /// <summary>Reads another process's heartbeat, or returns null when it has none.</summary>
    public static Reader? Open(int processId)
    {
        try
        {
            return new Reader(MemoryMappedFile.OpenExisting(NameFor(processId), MemoryMappedFileRights.Read));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void EndWaiting()
    {
        _view.Write(WaitingOffset, (long)Interlocked.Decrement(ref _waiting));
        Beat();
    }

    /// <summary>The watchdog's view of a Paddock's heartbeat.</summary>
    public sealed class Reader : IDisposable
    {
        private readonly MemoryMappedFile _file;
        private readonly MemoryMappedViewAccessor _view;

        internal Reader(MemoryMappedFile file)
        {
            _file = file;
            _view = file.CreateViewAccessor(0, Size, MemoryMappedFileAccess.Read);
        }

        /// <summary>When the UI thread last beat (GetTickCount64 milliseconds).</summary>
        public long LastBeat => _view.ReadInt64(BeatOffset);

        /// <summary>The UI thread is blocked on purpose.</summary>
        public bool WaitingOnPurpose => _view.ReadInt64(WaitingOffset) > 0;

        public void Dispose()
        {
            _view.Dispose();
            _file.Dispose();
        }
    }

    private sealed class WaitScope(AppHeartbeat owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner.EndWaiting();
        }
    }
}
