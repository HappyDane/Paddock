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
        return new IconStore(DesktopRoot, StoreRoot, CommonDesktopRoot);
    }

    /// <summary>Removes originals from the all-users desktop, asking for permission when needed.</summary>
    public ISharedDesktopRemover CreateSharedDesktopRemover() => new SharedDesktopRemover(CommonDesktopRoot);

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
    /// Watches the store for changes made outside the app (files dropped into a
    /// paddock folder from Explorer, items deleted, and so on). The callback is
    /// debounced and raised on a background thread — marshal to the UI yourself.
    /// </summary>
    public void WatchStore(Action onChanged)
    {
        _onStoreChanged = onChanged;

        try
        {
            EnsureHiddenStoreRoot();

            _debounce = new System.Timers.Timer(400) { AutoReset = false };
            _debounce.Elapsed += (_, _) => _onStoreChanged?.Invoke();

            _watcher = new FileSystemWatcher(StoreRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnStoreEvent;
            _watcher.Deleted += OnStoreEvent;
            _watcher.Renamed += OnStoreEvent;
        }
        catch (Exception ex)
        {
            Log.Error($"Cannot watch '{StoreRoot}' for changes.", ex);
        }
    }

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
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnStoreEvent;
            _watcher.Deleted -= OnStoreEvent;
            _watcher.Renamed -= OnStoreEvent;
            _watcher.Dispose();
            _watcher = null;
        }

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
