using System.IO;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Knows where the desktop lives and where paddocks keep their items.
///
/// Paddock stores the items it holds in a hidden folder on the desktop itself
/// (<c>%USERPROFILE%\Desktop\.Paddock\&lt;paddock-id&gt;</c>). Moving a file in
/// there is what makes its icon leave the desktop, because the shell only draws
/// the top-level entries of the Desktop folder. Keeping the folder on the
/// desktop — rather than off in AppData — means the user's files never travel
/// far, and stay recoverable even without the app.
/// </summary>
public sealed class DesktopIconService : IDisposable
{
    /// <summary>Name of the hidden folder holding one subfolder per paddock.</summary>
    public const string StoreFolderName = ".Paddock";

    private static readonly string[] IgnoredNames = ["desktop.ini", "thumbs.db"];

    private readonly object _watchGate = new();
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounce;
    private Action? _onStoreChanged;

    public DesktopIconService()
    {
        DesktopRoot = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        CommonDesktopRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        StoreRoot = Path.Combine(DesktopRoot, StoreFolderName);
    }

    /// <summary>The current user's Desktop folder.</summary>
    public string DesktopRoot { get; }

    /// <summary>The all-users Desktop folder (read-only for our purposes).</summary>
    public string CommonDesktopRoot { get; }

    /// <summary>Parent of the per-paddock folders.</summary>
    public string StoreRoot { get; }

    /// <summary>
    /// Creates the store folder (hidden, so it does not itself appear as a
    /// desktop icon) and returns a store bound to it.
    /// </summary>
    public IconStore CreateIconStore()
    {
        EnsureHiddenStoreRoot();
        var store = new IconStore(DesktopRoot, StoreRoot, CommonDesktopRoot);
        store.EnsureReadme();
        return store;
    }

    /// <summary>
    /// Removes originals from the all-users desktop, asking for permission when
    /// needed; <paramref name="waits"/> hears about the wait for the answer.
    /// </summary>
    public ISharedDesktopRemover CreateSharedDesktopRemover(IWaitAnnouncer? waits = null)
        => new SharedDesktopRemover(CommonDesktopRoot, waits);

    /// <summary>
    /// Top-level items shown on this user's desktop — files and folders, minus
    /// shell bookkeeping files and Paddock's own store folder — including the
    /// files on the all-users desktop, which Windows shows here too. (Taking
    /// one of those into a paddock asks for permission; see
    /// <see cref="SharedDesktopRemover"/>.)
    /// </summary>
    public IReadOnlyList<string> GetDesktopItems()
    {
        var items = new List<string>();

        AddVisibleEntries(DesktopRoot, items, filesOnly: false);

        if (!string.IsNullOrEmpty(CommonDesktopRoot)
            && !string.Equals(CommonDesktopRoot, DesktopRoot, StringComparison.OrdinalIgnoreCase))
        {
            AddVisibleEntries(CommonDesktopRoot, items, filesOnly: true);
        }

        return items;
    }

    private void AddVisibleEntries(string folder, List<string> items, bool filesOnly)
    {
        if (!Directory.Exists(folder))
            return;

        try
        {
            var entries = filesOnly
                ? Directory.EnumerateFiles(folder)
                : Directory.EnumerateFileSystemEntries(folder);

            foreach (var entry in entries)
            {
                if (IsHiddenOrIgnored(entry))
                    continue;

                items.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot list the desktop folder '{folder}': {ex.Message}");
        }
    }

    /// <summary>
    /// Where each file and folder on the desktop is drawn: its path and the
    /// centre of its icon in physical screen pixels. Asks Explorer across
    /// processes, so call it off the UI thread. Empty when Explorer cannot say.
    /// </summary>
    public IReadOnlyList<DesktopIconSpot> GetIconSpots() => DesktopFolderView.GetIconSpots();

    /// <summary>
    /// Whether the desktop's own icons are showing, or <c>null</c> when the
    /// desktop's icon view cannot be found.
    /// </summary>
    public bool? AreDesktopIconsShown()
    {
        var listView = DesktopListView.Find();
        return listView == IntPtr.Zero ? null : NativeMethods.IsWindowVisible(listView);
    }

    /// <summary>
    /// Shows or hides the desktop's own icons the way the desktop menu's
    /// View → Show desktop icons does (Explorer remembers it, and the user can
    /// always undo it from that menu). Returns true when the icons end up in
    /// the requested state.
    /// </summary>
    public bool SetDesktopIconsShown(bool shown)
    {
        var listView = DesktopListView.Find();
        if (listView == IntPtr.Zero)
            return false;

        if (NativeMethods.IsWindowVisible(listView) == shown)
            return true;

        var view = NativeMethods.GetParent(listView);
        var answered = NativeMethods.SendMessageTimeoutW(
            view,
            NativeMethods.WM_COMMAND,
            NativeMethods.DesktopToggleIconsCommand,
            IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG,
            2000,
            out _) != IntPtr.Zero;

        var now = NativeMethods.IsWindowVisible(listView);
        if (now != shown)
            Log.Warn($"Could not {(shown ? "show" : "hide")} the desktop icons (Explorer {(answered ? "ignored" : "did not answer")} the request).");

        return now == shown;
    }

    /// <summary>The user's Downloads folder (wherever they moved it).</summary>
    public static string DownloadsFolder
    {
        get
        {
            try
            {
                if (NativeMethods.SHGetKnownFolderPath(NativeMethods.FOLDERID_Downloads, 0, IntPtr.Zero, out var pointer) == 0)
                {
                    try
                    {
                        var path = System.Runtime.InteropServices.Marshal.PtrToStringUni(pointer);
                        if (!string.IsNullOrEmpty(path))
                            return path;
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pointer);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Cannot look up the Downloads folder: {ex.Message}");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    /// <summary>Watches for items arriving on the desktop, for the auto-organize rules.</summary>
    public DesktopArrivalWatcher CreateArrivalWatcher() => new(DesktopRoot, StoreRoot);

    /// <summary>
    /// Watches the store for changes made outside the app (files dropped into a
    /// paddock folder from Explorer, items deleted, and so on). The callback is
    /// debounced and raised on a background thread — marshal to the UI yourself.
    /// </summary>
    public void WatchStore(Action onChanged)
    {
        _onStoreChanged = onChanged;
        _debounce ??= new System.Timers.Timer(400) { AutoReset = false };
        _debounce.Elapsed -= OnDebounceElapsed;
        _debounce.Elapsed += OnDebounceElapsed;

        StartStoreWatcher();
    }

    /// <summary>
    /// Makes sure the store is still being watched — that the folder exists
    /// (the user may have deleted it) and the watcher has not stopped. Cheap;
    /// called periodically. A restarted watcher also asks for a re-sync, since
    /// changes may have gone unseen.
    /// </summary>
    public void EnsureStoreWatched()
    {
        if (_onStoreChanged is null)
            return;

        if (Directory.Exists(StoreRoot) && _watcher is { EnableRaisingEvents: true })
            return;

        Log.Warn("The paddock store was no longer being watched; watching it again.");
        StartStoreWatcher();
        _onStoreChanged?.Invoke();
    }

    private void StartStoreWatcher()
    {
        lock (_watchGate)
        {
            StartStoreWatcherLocked();
        }
    }

    private void StartStoreWatcherLocked()
    {
        StopStoreWatcherLocked();

        try
        {
            EnsureHiddenStoreRoot();

            _watcher = new FileSystemWatcher(StoreRoot)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnStoreEvent;
            _watcher.Deleted += OnStoreEvent;
            _watcher.Renamed += OnStoreEvent;
            _watcher.Error += OnStoreError;
        }
        catch (Exception ex)
        {
            Log.Error($"Cannot watch '{StoreRoot}' for changes.", ex);
        }
    }

    private void StopStoreWatcher()
    {
        lock (_watchGate)
        {
            StopStoreWatcherLocked();
        }
    }

    private void StopStoreWatcherLocked()
    {
        if (_watcher is null)
            return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnStoreEvent;
        _watcher.Deleted -= OnStoreEvent;
        _watcher.Renamed -= OnStoreEvent;
        _watcher.Error -= OnStoreError;
        _watcher.Dispose();
        _watcher = null;
    }

    /// <summary>
    /// Too many changes at once (the watcher's buffer overflowed) or the
    /// folder went away: some changes were missed. Re-sync from disk, and
    /// start watching afresh if the watcher gave up.
    /// </summary>
    private void OnStoreError(object sender, ErrorEventArgs e)
    {
        Log.Warn($"Missed some changes in the paddock store ({e.GetException().Message}); re-reading it.");

        if (_watcher is { EnableRaisingEvents: false } || !Directory.Exists(StoreRoot))
            StartStoreWatcher();

        _onStoreChanged?.Invoke();
    }

    private void OnDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e) => _onStoreChanged?.Invoke();

    /// <summary>
    /// Tells the shell a folder's contents changed, so the desktop redraws
    /// promptly after items are moved in or out.
    /// </summary>
    public void NotifyShellOfDesktopChange()
    {
        var pathPtr = IntPtr.Zero;
        try
        {
            pathPtr = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(DesktopRoot);
            NativeMethods.SHChangeNotify(
                NativeMethods.SHCNE_UPDATEDIR,
                NativeMethods.SHCNF_PATHW,
                pathPtr,
                IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn($"SHChangeNotify failed: {ex.Message}");
        }
        finally
        {
            if (pathPtr != IntPtr.Zero)
                System.Runtime.InteropServices.Marshal.FreeHGlobal(pathPtr);
        }
    }

    public void Dispose()
    {
        StopStoreWatcher();

        _debounce?.Dispose();
        _debounce = null;
        _onStoreChanged = null;
    }

    private void OnStoreEvent(object sender, FileSystemEventArgs e)
    {
        // Collapse bursts of events (a multi-file move) into one refresh.
        if (_debounce is null)
            return;

        _debounce.Stop();
        _debounce.Start();
    }

    private void EnsureHiddenStoreRoot()
    {
        try
        {
            var info = Directory.CreateDirectory(StoreRoot);
            if (!info.Attributes.HasFlag(FileAttributes.Hidden))
                info.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception ex)
        {
            Log.Error($"Cannot prepare the paddock store at '{StoreRoot}'.", ex);
        }
    }

    private bool IsHiddenOrIgnored(string path)
    {
        var name = Path.GetFileName(path);

        if (IgnoredNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return true;

        if (string.Equals(name, StoreFolderName, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System);
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot read attributes of '{path}': {ex.Message}");
            return true;
        }
    }
}
