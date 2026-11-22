using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Notices items arriving on the user's desktop — a download, a file saved or
/// dragged there, a shortcut an installer made — so the auto-organize rules
/// can put them in a paddock.
///
/// Three things must never be mistaken for an arrival, and are not:
/// <list type="bullet">
/// <item>An item coming back <em>out of a paddock</em> (the user dragged it out,
/// or Paddock put it back). One recursive watcher covers the desktop and the
/// paddock store inside it, so such a move shows as a rename out of the store;
/// <see cref="IgnoreReturned"/> and a short memory of what left the store
/// cover the rest.</item>
/// <item>An item that is still being written (a download or copy in progress):
/// it is only looked at once nothing has happened to it for a moment and it
/// can actually be moved.</item>
/// <item>An item being renamed in Explorer (a new folder, say): it waits until
/// the user has finished typing.</item>
/// </list>
///
/// It also remembers what recently left the folders the rules care about
/// (<see cref="SetSourceFolders"/>, typically Downloads), so a rule can say
/// "anything that came from Downloads". Events are raised on a worker thread.
/// </summary>
public sealed class DesktopArrivalWatcher : IDisposable
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan Memory = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(15);
    private const int TickMs = 500;

    private static readonly string[] IgnoredNames = ["desktop.ini", "thumbs.db"];

    /// <summary>Names browsers and copy tools give a file until it is complete.</summary>
    private static readonly string[] TemporaryExtensions =
        [".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp", ".!ut", ".aria2"];

    private static readonly Regex CopySuffix = new(@"^(?<stem>.*) \(\d+\)(?<ext>\.[^.]*)?$", RegexOptions.CultureInvariant);

    private readonly string _desktopRoot;
    private readonly string _storeName;
    private readonly object _gate = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Departure> _departures = new();
    private readonly Dictionary<string, DateTime> _returned = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> _sources = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _desktop;
    private Timer? _timer;
    private bool _scheduled;
    private bool _disposed;

    internal DesktopArrivalWatcher(string desktopRoot, string storeRoot)
    {
        _desktopRoot = Path.GetFullPath(desktopRoot).TrimEnd(Path.DirectorySeparatorChar);
        _storeName = Path.GetFileName(Path.GetFullPath(storeRoot).TrimEnd(Path.DirectorySeparatorChar));
    }

    /// <summary>New items that have settled on the desktop. Raised on a worker thread.</summary>
    public event Action<IReadOnlyList<DesktopItemFacts>>? ItemsArrived;

    public void Start()
    {
        lock (_gate)
        {
            if (_desktop is not null || _disposed)
                return;

            _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);

            try
            {
                _desktop = new FileSystemWatcher(_desktopRoot)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                                   | NotifyFilters.Size | NotifyFilters.LastWrite,
                };

                _desktop.Created += (_, e) => OnDesktopEvent(e.FullPath, created: true);
                _desktop.Changed += (_, e) => OnDesktopEvent(e.FullPath, created: false);
                _desktop.Deleted += (_, e) => OnDesktopDeleted(e.FullPath);
                _desktop.Renamed += (_, e) => OnDesktopRenamed(e.OldFullPath, e.FullPath);
                _desktop.Error += (_, e) => Log.Warn($"Missed some desktop changes: {e.GetException().Message}");
                _desktop.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.Error($"Cannot watch the desktop for new items ('{_desktopRoot}').", ex);
            }
        }
    }

    /// <summary>The folders whose departures are remembered, for "came from folder" rules.</summary>
    public void SetSourceFolders(IEnumerable<string> folders)
    {
        var wanted = folders
            .Select(f => f.TrimEnd(Path.DirectorySeparatorChar))
            .Where(f => !IsInsideDesktop(f)) // the desktop watcher already sees those
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            if (_disposed)
                return;

            foreach (var gone in _sources.Keys.Where(k => !wanted.Contains(k)).ToList())
            {
                _sources[gone].Dispose();
                _sources.Remove(gone);
            }

            foreach (var folder in wanted.Where(f => !_sources.ContainsKey(f)))
            {
                if (!Directory.Exists(folder))
                {
                    Log.Warn($"Auto-organize: the folder '{folder}' does not exist, so nothing can come from it.");
                    continue;
                }

                try
                {
                    var watcher = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    };

                    watcher.Deleted += (_, e) => Remember(folder, Path.GetFileName(e.FullPath));
                    watcher.Renamed += (_, e) =>
                    {
                        if (!string.Equals(Path.GetDirectoryName(e.FullPath), folder, StringComparison.OrdinalIgnoreCase))
                            Remember(folder, Path.GetFileName(e.OldFullPath));
                    };
                    watcher.EnableRaisingEvents = true;
                    _sources[folder] = watcher;
                }
                catch (Exception ex)
                {
                    Log.Warn($"Cannot watch '{folder}' for auto-organize: {ex.Message}");
                }
            }
        }
    }

    /// <summary>Paddock itself just put this item back on the desktop: leave it there.</summary>
    public void IgnoreReturned(string desktopPath)
    {
        lock (_gate)
        {
            _returned[Normalize(desktopPath)] = DateTime.UtcNow;
            _pending.Remove(Normalize(desktopPath));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _desktop?.Dispose();
            _desktop = null;

            foreach (var watcher in _sources.Values)
                watcher.Dispose();

            _sources.Clear();
            _pending.Clear();
            _timer?.Dispose();
            _timer = null;
        }
    }

    // --- Desktop events (raised on watcher threads) ---

    private void OnDesktopEvent(string path, bool created)
    {
        var (top, isTopLevel) = TopLevelItem(path);
        if (top is null)
            return;

        lock (_gate)
        {
            if (isTopLevel && created && !IsStore(top) && !IsIgnoredName(top))
            {
                Touch(top);
                return;
            }

            // Something inside (or about) an item still settling: wait longer.
            if (_pending.TryGetValue(top, out var pending))
                pending.LastEvent = DateTime.UtcNow;
        }
    }

    private void OnDesktopDeleted(string path)
    {
        var (top, isTopLevel) = TopLevelItem(path);
        if (top is null)
            return;

        lock (_gate)
        {
            if (isTopLevel)
            {
                _pending.Remove(top);
                return;
            }

            // An item left a paddock; if it reappears on the desktop, it came back from there.
            if (IsStore(top) && IsPaddockItem(path))
                Remember(null, Path.GetFileName(path));
            else if (_pending.TryGetValue(top, out var pending))
                pending.LastEvent = DateTime.UtcNow;
        }
    }

    private void OnDesktopRenamed(string oldPath, string newPath)
    {
        var (oldTop, oldIsTop) = TopLevelItem(oldPath);
        var (newTop, newIsTop) = TopLevelItem(newPath);

        lock (_gate)
        {
            // Out of a paddock, back onto the desktop: the user (or Paddock) wants it there.
            if (oldTop is not null && IsStore(oldTop))
            {
                if (newIsTop && newTop is not null)
                {
                    _returned[newTop] = DateTime.UtcNow;
                    _pending.Remove(newTop);
                }
                else if (IsPaddockItem(oldPath))
                {
                    Remember(null, Path.GetFileName(oldPath));
                }

                return;
            }

            // A desktop item went somewhere else (into a paddock, into a folder).
            if (oldIsTop && oldTop is not null && !(newIsTop && newTop is not null))
            {
                _pending.Remove(oldTop);

                if (newTop is not null && _pending.TryGetValue(newTop, out var container))
                    container.LastEvent = DateTime.UtcNow;

                return;
            }

            if (!newIsTop || newTop is null || IsStore(newTop) || IsIgnoredName(newTop))
            {
                if (newTop is not null && _pending.TryGetValue(newTop, out var settling))
                    settling.LastEvent = DateTime.UtcNow;
                return;
            }

            if (oldIsTop && oldTop is not null)
            {
                // Renamed on the desktop. A brand-new item keeps settling under
                // its new name; a finished download is new now; anything else
                // was already there, and a rename does not make it new.
                if (_pending.Remove(oldTop, out var existing))
                {
                    existing.LastEvent = DateTime.UtcNow;
                    _pending[newTop] = existing;
                }
                else if (IsTemporaryName(oldTop))
                {
                    Touch(newTop);
                }

                return;
            }

            // Moved up out of a folder that is itself on the desktop.
            Touch(newTop).CameFrom = oldTop is null ? null : Path.GetDirectoryName(oldPath);
        }
    }

    // --- Deciding (timer thread) ---

    private void Tick()
    {
        try
        {
            var arrived = Settle();
            if (arrived.Count > 0)
            {
                var facts = arrived.Select(a => DesktopItemInspector.Inspect(a.Path, a.CameFrom)).ToList();
                ItemsArrived?.Invoke(facts);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Checking new desktop items failed.", ex);
        }
        finally
        {
            lock (_gate)
            {
                _scheduled = false;
                if (_pending.Count > 0)
                    Schedule();
            }
        }
    }

    /// <summary>Takes the pending items that are ready, and drops those that never will be.</summary>
    private List<(string Path, string? CameFrom)> Settle()
    {
        var now = DateTime.UtcNow;
        List<(string Path, Pending Item)> due;

        lock (_gate)
        {
            _departures.RemoveAll(d => now - d.Time > Memory);
            foreach (var stale in _returned.Where(r => now - r.Value > Memory).Select(r => r.Key).ToList())
                _returned.Remove(stale);

            due = _pending.Where(p => now - p.Value.LastEvent >= QuietPeriod).Select(p => (p.Key, p.Value)).ToList();
        }

        var ready = new List<(string, string?)>();
        if (due.Count == 0)
            return ready;

        var renaming = DesktopListView.IsRenamingAnIcon();
        var finished = new List<(string Path, Pending Item)>();

        foreach (var (path, item) in due)
        {
            var name = Path.GetFileName(path);

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                finished.Add((path, item));
                continue;
            }

            lock (_gate)
            {
                if (_returned.ContainsKey(path) || DepartedFrom(null, name, item.FirstSeen))
                {
                    Log.Info($"Auto-organize: '{name}' came out of a paddock, so it stays on the desktop.");
                    finished.Add((path, item));
                    continue;
                }
            }

            if (IsHiddenOrSystem(path) || now - item.FirstSeen > GiveUpAfter)
            {
                finished.Add((path, item));
                continue;
            }

            // Still being typed, written or copied: look again shortly.
            if (renaming || IsBusy(path))
                continue;

            finished.Add((path, item));
            ready.Add((path, item.CameFrom ?? CameFrom(name, path, item.FirstSeen)));
        }

        lock (_gate)
        {
            foreach (var (path, item) in finished)
            {
                // Untouched since we looked? Then it is done with; otherwise it keeps settling.
                if (_pending.TryGetValue(path, out var current) && ReferenceEquals(current, item) && current.LastEvent <= now)
                    _pending.Remove(path);
            }
        }

        return ready;
    }

    /// <summary>The watched folder an item was moved out of, or still sits in as the original of a copy.</summary>
    private string? CameFrom(string name, string path, DateTime firstSeen)
    {
        List<string> folders;

        lock (_gate)
        {
            var moved = _departures.FirstOrDefault(d => d.Folder is not null && Matches(d.Name, name) && Near(d.Time, firstSeen));
            if (moved is not null)
                return moved.Folder;

            folders = _sources.Keys.ToList();
        }

        foreach (var folder in folders)
        {
            var original = Path.Combine(folder, name);

            try
            {
                if (Directory.Exists(path) ? Directory.Exists(original)
                    : File.Exists(original) && new FileInfo(original).Length == new FileInfo(path).Length)
                {
                    return folder;
                }
            }
            catch (IOException)
            {
                // Gone again in between: not a copy of anything we can see.
            }
        }

        return null;
    }

    // --- Helpers (call with _gate held where they touch state) ---

    private Pending Touch(string path)
    {
        var now = DateTime.UtcNow;
        if (!_pending.TryGetValue(path, out var pending))
        {
            pending = new Pending { FirstSeen = now };
            _pending[path] = pending;
        }

        pending.LastEvent = now;
        Schedule();
        return pending;
    }

    private void Schedule()
    {
        if (_scheduled || _disposed)
            return;

        _scheduled = true;
        _timer?.Change(TickMs, Timeout.Infinite);
    }

    private void Remember(string? folder, string name)
    {
        lock (_gate)
        {
            _departures.Add(new Departure(folder, name, DateTime.UtcNow));
        }
    }

    private bool DepartedFrom(string? folder, string name, DateTime around)
        => _departures.Any(d => string.Equals(d.Folder, folder, StringComparison.OrdinalIgnoreCase)
                                && Matches(d.Name, name)
                                && Near(d.Time, around));

    /// <summary>Same name — or the same name with Explorer's " (2)" added to avoid a clash.</summary>
    private static bool Matches(string departed, string arrived)
    {
        if (string.Equals(departed, arrived, StringComparison.OrdinalIgnoreCase))
            return true;

        var match = CopySuffix.Match(arrived);
        return match.Success
               && string.Equals(match.Groups["stem"].Value + match.Groups["ext"].Value, departed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Near(DateTime a, DateTime b) => (a > b ? a - b : b - a) <= Memory;

    /// <summary>
    /// The top-level desktop item an event is about (as a full path) and
    /// whether the event is about that item itself; null when it is outside.
    /// </summary>
    private (string? Top, bool IsTopLevel) TopLevelItem(string path)
    {
        string relative;

        try
        {
            relative = Path.GetRelativePath(_desktopRoot, Path.GetFullPath(path));
        }
        catch (Exception)
        {
            return (null, false);
        }

        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return (null, false);

        var separator = relative.IndexOf(Path.DirectorySeparatorChar);
        var first = separator < 0 ? relative : relative[..separator];
        return (Path.Combine(_desktopRoot, first), separator < 0);
    }

    private bool IsStore(string top) => string.Equals(Path.GetFileName(top), _storeName, StringComparison.OrdinalIgnoreCase);

    /// <summary>An item directly inside a paddock's folder: Desktop\.Paddock\&lt;id&gt;\&lt;item&gt;.</summary>
    private bool IsPaddockItem(string path)
        => Path.GetRelativePath(_desktopRoot, path).Split(Path.DirectorySeparatorChar).Length == 3;

    private bool IsInsideDesktop(string folder)
    {
        var relative = Path.GetRelativePath(_desktopRoot, folder);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    private static bool IsIgnoredName(string path)
    {
        var name = Path.GetFileName(path);
        return IgnoredNames.Contains(name, StringComparer.OrdinalIgnoreCase)
               || name.StartsWith("~$", StringComparison.Ordinal)
               || IsTemporaryName(path);
    }

    private static bool IsTemporaryName(string path)
        => TemporaryExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsHiddenOrSystem(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// True while something holds the file in a way that would stop it being
    /// moved — a download or copy still writing it. Folders are judged by the
    /// quiet period alone.
    /// </summary>
    private static bool IsBusy(string path)
    {
        if (Directory.Exists(path))
            return false;

        // Firefox saves into "report.pdf.part" beside an empty "report.pdf"
        // placeholder: the placeholder is not the download, so leave it be.
        if (TemporaryExtensions.Any(extension => File.Exists(path + extension)))
            return true;

        using var handle = NativeMethods.CreateFile(
            path,
            NativeMethods.DELETE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        return handle.IsInvalid && Marshal.GetLastWin32Error() == NativeMethods.ERROR_SHARING_VIOLATION;
    }

    private string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private sealed class Pending
    {
        public DateTime FirstSeen { get; init; }

        public DateTime LastEvent { get; set; }

        public string? CameFrom { get; set; }
    }

    /// <param name="Folder">The watched folder it left, or null for a paddock.</param>
    private sealed record Departure(string? Folder, string Name, DateTime Time);
}
