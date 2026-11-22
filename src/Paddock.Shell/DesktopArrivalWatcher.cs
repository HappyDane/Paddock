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
/// Most of the work is in what must <em>not</em> count as an arrival:
/// <list type="bullet">
/// <item>An item coming back out of a paddock — dragged out (moved, copied or
/// linked) by the user, or put back by Paddock. A move out of the paddock
/// store reaches the desktop as "removed from the store, added to the
/// desktop", so what leaves the store is remembered for a moment;
/// <see cref="IgnoreReturned"/> and <see cref="ExpectItemsFromPaddock"/> cover
/// Paddock's own moves and drags.</item>
/// <item>A document being <em>saved</em>. Word, Excel and many others save by
/// writing a temporary file and renaming it over the original, which looks
/// like a new file. An item that takes the name of something that was on the
/// desktop moments ago is a save, not an arrival.</item>
/// <item>Something unfinished: a download or copy still writing (the file
/// cannot be opened for moving yet, or a <c>.part</c> sibling exists), an
/// empty placeholder (the New Shortcut wizard's, say), or an item the user is
/// naming anywhere in the shell.</item>
/// </list>
/// These verdicts are reached when the events happen and kept with the
/// pending item, so they do not depend on how long the item takes to settle.
///
/// It also remembers what recently left the folders the rules care about
/// (<see cref="SetSourceFolders"/>, typically Downloads), so a rule can say
/// "anything that came from Downloads". Events are raised on a worker thread.
/// </summary>
public sealed class DesktopArrivalWatcher : IDisposable
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(1.5);

    /// <summary>An empty file is usually a placeholder for something still to come.</summary>
    private static readonly TimeSpan EmptyQuietPeriod = TimeSpan.FromSeconds(30);

    /// <summary>How long a departure, a vacated name or a drag is remembered for matching.</summary>
    private static readonly TimeSpan Memory = TimeSpan.FromSeconds(20);

    /// <summary>After a drag out of a paddock ends, whatever lands on the desktop this soon came from it.</summary>
    private static readonly TimeSpan DragGrace = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(15);
    private const int TickMs = 500;

    private static readonly string[] IgnoredNames = ["desktop.ini", "thumbs.db"];

    /// <summary>Names browsers, copy tools and editors give a file until it is complete.</summary>
    private static readonly string[] TemporaryExtensions =
        [".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp", ".!ut", ".aria2"];

    private static readonly string[] ShortcutExtensions = [".lnk", ".url"];

    private static readonly Regex CopySuffix = new(@"^(?<stem>.*) \(\d+\)(?<ext>\.[^.]*)?$", RegexOptions.CultureInvariant);

    private readonly string _desktopRoot;
    private readonly string _storeName;
    private readonly object _gate = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Departure> _departures = new();

    /// <summary>Names of established desktop items that just left (deleted, renamed or moved away).</summary>
    private readonly Dictionary<string, DateTime> _vacated = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Paths Paddock itself just put back on the desktop.</summary>
    private readonly Dictionary<string, DateTime> _returned = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, FileSystemWatcher> _sources = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _desktop;
    private Timer? _timer;
    private bool _scheduled;
    private bool _disposed;
    private int _dragsOut;
    private DateTime _dragOutEnded = DateTime.MinValue;

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
            .Where(f => !string.Equals(f, _desktopRoot, StringComparison.OrdinalIgnoreCase))
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
                    // Non-recursive: a move out of the folder is reported to it as a removal.
                    var watcher = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    };

                    watcher.Deleted += (_, e) => Departed(folder, Path.GetFileName(e.FullPath));
                    watcher.Renamed += (_, e) =>
                    {
                        if (!string.Equals(Path.GetDirectoryName(e.FullPath), folder, StringComparison.OrdinalIgnoreCase))
                            Departed(folder, Path.GetFileName(e.OldFullPath));
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
        var path = Normalize(desktopPath);

        lock (_gate)
        {
            _returned[path] = DateTime.UtcNow;

            if (_pending.TryGetValue(path, out var pending))
                pending.FromPaddock = true;
        }
    }

    /// <summary>
    /// An icon is being dragged out of a paddock. Until the returned scope is
    /// disposed, and briefly after, whatever appears on the desktop came from
    /// the paddock — moved, copied or as a new shortcut — and stays where the
    /// user dropped it.
    /// </summary>
    public IDisposable ExpectItemsFromPaddock()
    {
        lock (_gate)
        {
            _dragsOut++;
        }

        return new DragScope(this);
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
            if (isTopLevel && created && IsCandidate(top))
            {
                Arrived(top);
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
                // A brand-new item going again is nothing; an established one
                // leaving may be an app saving over it.
                if (!_pending.Remove(top))
                    _vacated[Path.GetFileName(top)] = DateTime.UtcNow;

                return;
            }

            // An item left a paddock; if it reappears on the desktop, it came back from there.
            if (IsStore(top) && IsPaddockItem(path))
            {
                Departed(null, Path.GetFileName(path));
                return;
            }

            if (_pending.TryGetValue(top, out var pending))
                pending.LastEvent = DateTime.UtcNow;
        }
    }

    private void OnDesktopRenamed(string oldPath, string newPath)
    {
        var (oldTop, oldIsTop) = TopLevelItem(oldPath);
        var (newTop, newIsTop) = TopLevelItem(newPath);
        var lands = newIsTop && newTop is not null && IsCandidate(newTop);

        lock (_gate)
        {
            if (oldTop is not null && IsStore(oldTop))
            {
                // Straight out of a paddock onto the desktop: the user wants it there.
                if (newIsTop && newTop is not null)
                {
                    _returned[newTop] = DateTime.UtcNow;
                    if (_pending.TryGetValue(newTop, out var back))
                        back.FromPaddock = true;
                }
                else if (IsPaddockItem(oldPath))
                {
                    Departed(null, Path.GetFileName(oldPath));
                }

                return;
            }

            if (oldIsTop && oldTop is not null)
            {
                var wasPending = _pending.Remove(oldTop, out var existing);
                if (!wasPending)
                    _vacated[Path.GetFileName(oldTop)] = DateTime.UtcNow;

                if (!lands)
                {
                    // It went into a folder, a paddock, or to a temporary name.
                    if (newTop is not null && _pending.TryGetValue(newTop, out var container))
                        container.LastEvent = DateTime.UtcNow;

                    return;
                }

                // A save renames its new copy onto the name the old one had.
                if (WasVacated(newTop!))
                {
                    _pending.Remove(newTop!);
                    return;
                }

                if (wasPending)
                {
                    // Still brand new: it keeps settling under its new name.
                    existing!.LastEvent = DateTime.UtcNow;
                    _pending[newTop!] = existing;
                }
                else if (IsTemporaryName(oldTop))
                {
                    // A finished download. Renaming an item that was already
                    // there does not make it new.
                    Arrived(newTop!);
                }

                return;
            }

            if (lands)
            {
                // Up out of a folder that is itself on the desktop.
                if (Arrived(newTop!) is { } pending && oldTop is not null)
                    pending.CameFrom ??= Path.GetDirectoryName(oldPath);
            }
            else if (newTop is not null && _pending.TryGetValue(newTop, out var settling))
            {
                settling.LastEvent = DateTime.UtcNow;
            }
        }
    }

    // --- Bookkeeping (call with _gate held) ---

    /// <summary>
    /// Something appeared under this top-level name. Decides at once what it
    /// is — a save over an existing item, something out of a paddock, or new —
    /// and returns the pending entry when it is worth waiting for.
    /// </summary>
    private Pending? Arrived(string top)
    {
        if (WasVacated(top))
        {
            _pending.Remove(top);
            return null;
        }

        var now = DateTime.UtcNow;
        var isNew = !_pending.TryGetValue(top, out var pending);
        if (isNew)
        {
            pending = new Pending { FirstSeen = now };
            _pending[top] = pending;
        }

        pending!.LastEvent = now;

        var name = Path.GetFileName(top);
        if (_returned.ContainsKey(top) || DepartedFrom(null, name) || IsDraggingOut(now))
            pending.FromPaddock = true;

        pending.CameFrom ??= _departures.FirstOrDefault(d => d.Folder is not null && Matches(d.Name, name))?.Folder;

        Schedule();
        return pending;
    }

    /// <summary>Something left a paddock (<paramref name="folder"/> null) or a watched folder.</summary>
    private void Departed(string? folder, string name)
    {
        lock (_gate)
        {
            _departures.Add(new Departure(folder, name, DateTime.UtcNow));

            // The arrival may have been reported first.
            foreach (var (path, pending) in _pending)
            {
                if (!Matches(name, Path.GetFileName(path)))
                    continue;

                if (folder is null)
                    pending.FromPaddock = true;
                else
                    pending.CameFrom ??= folder;
            }
        }
    }

    private bool WasVacated(string top)
        => _vacated.TryGetValue(Path.GetFileName(top), out var when) && DateTime.UtcNow - when <= Memory;

    private bool DepartedFrom(string? folder, string name)
        => _departures.Any(d => string.Equals(d.Folder, folder, StringComparison.OrdinalIgnoreCase) && Matches(d.Name, name));

    private bool IsDraggingOut(DateTime now) => _dragsOut > 0 || now - _dragOutEnded <= DragGrace;

    private void EndDragOut()
    {
        lock (_gate)
        {
            if (_dragsOut > 0)
                _dragsOut--;

            _dragOutEnded = DateTime.UtcNow;
        }
    }

    private void Schedule()
    {
        if (_scheduled || _disposed)
            return;

        _scheduled = true;
        _timer?.Change(TickMs, Timeout.Infinite);
    }

    private void Forget(DateTime now)
    {
        _departures.RemoveAll(d => now - d.Time > Memory);

        foreach (var stale in _returned.Where(r => now - r.Value > Memory).Select(r => r.Key).ToList())
            _returned.Remove(stale);

        foreach (var stale in _vacated.Where(v => now - v.Value > Memory).Select(v => v.Key).ToList())
            _vacated.Remove(stale);
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
            Forget(now);
            due = _pending.Where(p => now - p.Value.LastEvent >= QuietPeriod).Select(p => (p.Key, p.Value)).ToList();
        }

        var ready = new List<(string, string?)>();
        if (due.Count == 0)
            return ready;

        var naming = DesktopListView.IsRenamingAnItem();
        var finished = new List<(string Path, Pending Item)>();

        foreach (var (path, item) in due)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                finished.Add((path, item));
                continue;
            }

            if (item.FromPaddock)
            {
                Log.Info($"Auto-organize: '{Path.GetFileName(path)}' came out of a paddock, so it stays on the desktop.");
                finished.Add((path, item));
                continue;
            }

            if (IsHiddenOrSystem(path) || now - item.FirstSeen > GiveUpAfter)
            {
                finished.Add((path, item));
                continue;
            }

            // Still being named, written, copied or filled in: look again shortly.
            if (naming || IsBusy(path) || IsPlaceholder(path, now - item.LastEvent))
                continue;

            finished.Add((path, item));
            ready.Add((path, item.CameFrom ?? CopiedFrom(path)));
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

    /// <summary>A watched folder that still holds the original this item is a copy of.</summary>
    private string? CopiedFrom(string path)
    {
        List<string> folders;

        lock (_gate)
        {
            folders = _sources.Keys.ToList();
        }

        var name = Path.GetFileName(path);

        foreach (var folder in folders)
        {
            var original = Path.Combine(folder, name);

            try
            {
                if (Directory.Exists(path)
                        ? Directory.Exists(original)
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

    // --- Helpers ---

    /// <summary>Same name — or the same name with Explorer's " (2)" added to avoid a clash.</summary>
    private static bool Matches(string departed, string arrived)
    {
        if (string.Equals(departed, arrived, StringComparison.OrdinalIgnoreCase))
            return true;

        var match = CopySuffix.Match(arrived);
        return match.Success
               && string.Equals(match.Groups["stem"].Value + match.Groups["ext"].Value, departed, StringComparison.OrdinalIgnoreCase);
    }

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

    /// <summary>A top-level name that could be a real new item (not the store, not a temporary file).</summary>
    private bool IsCandidate(string top) => !IsStore(top) && !IsIgnoredName(top);

    private bool IsStore(string top) => string.Equals(Path.GetFileName(top), _storeName, StringComparison.OrdinalIgnoreCase);

    /// <summary>An item directly inside a paddock's folder: Desktop\.Paddock\&lt;id&gt;\&lt;item&gt;.</summary>
    private bool IsPaddockItem(string path)
        => Path.GetRelativePath(_desktopRoot, path).Split(Path.DirectorySeparatorChar).Length == 3;

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
    /// An empty file is usually a placeholder: a download's (Firefox), a new
    /// document still being named, the New Shortcut wizard's. An empty
    /// shortcut is never a finished one; anything else empty gets a longer wait.
    /// </summary>
    private static bool IsPlaceholder(string path, TimeSpan quietFor)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > 0)
                return false;

            return ShortcutExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase) || quietFor < EmptyQuietPeriod;
        }
        catch (IOException)
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

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private sealed class Pending
    {
        public DateTime FirstSeen { get; init; }

        public DateTime LastEvent { get; set; }

        public string? CameFrom { get; set; }

        /// <summary>It came out of a paddock (or was dropped from one): never routed.</summary>
        public bool FromPaddock { get; set; }
    }

    /// <param name="Folder">The watched folder it left, or null for a paddock.</param>
    private sealed record Departure(string? Folder, string Name, DateTime Time);

    private sealed class DragScope(DesktopArrivalWatcher owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner.EndDragOut();
        }
    }
}
