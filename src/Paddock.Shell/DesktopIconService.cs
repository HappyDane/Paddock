using System.Diagnostics;
using System.IO;

namespace Paddock.Shell;

/// <summary>
/// Reads and manipulates desktop icon positions via the Windows Shell.
/// </summary>
public class DesktopIconService
{
    /// <summary>
    /// Gets the paths of all icons currently on the desktop.
    /// </summary>
    public List<string> GetDesktopIconPaths()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        var icons = new List<string>();

        if (Directory.Exists(desktopPath))
            icons.AddRange(Directory.GetFiles(desktopPath));

        if (Directory.Exists(commonDesktop))
            icons.AddRange(Directory.GetFiles(commonDesktop));

        return icons;
    }

    /// <summary>
    /// Hides a desktop icon by moving it off-screen.
    /// Used when an icon is placed inside a paddock.
    /// </summary>
    public void HideDesktopIcon(string path)
    {
        SetDesktopIconPosition(path, -10000, -10000);
    }

    /// <summary>
    /// Restores a desktop icon to a visible position.
    /// Used when an icon is removed from a paddock.
    /// </summary>
    public void ShowDesktopIcon(string path, int x, int y)
    {
        SetDesktopIconPosition(path, x, y);
    }

    /// <summary>
    /// Starts watching for new icons appearing on the desktop.
    /// </summary>
    public FileSystemWatcher? WatchDesktopChanges(Action<string> onNewIcon, Action<string> onDeletedIcon)
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!Directory.Exists(desktopPath))
            return null;

        var watcher = new FileSystemWatcher(desktopPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };

        watcher.Created += (_, e) => onNewIcon(e.FullPath);
        watcher.Deleted += (_, e) => onDeletedIcon(e.FullPath);

        return watcher;
    }

    /// <summary>
    /// Finds the SysListView32 (desktop icon ListView) handle.
    /// The desktop icon list is a child of SHELLDLL_DefView inside Progman or a WorkerW.
    /// </summary>
    private static IntPtr GetDesktopListView()
    {
        // First try: Progman > SHELLDLL_DefView > SysListView32
        var progman = NativeMethods.FindWindowW("Progman", null);
        if (progman != IntPtr.Zero)
        {
            var shellView = NativeMethods.FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                var listView = NativeMethods.FindWindowExW(shellView, IntPtr.Zero, "SysListView32", null);
                if (listView != IntPtr.Zero)
                    return listView;
            }
        }

        // Fallback: SHELLDLL_DefView might be under a WorkerW window
        IntPtr result = IntPtr.Zero;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            var shellView = NativeMethods.FindWindowExW(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                result = NativeMethods.FindWindowExW(shellView, IntPtr.Zero, "SysListView32", null);
                if (result != IntPtr.Zero)
                    return false; // stop enumerating
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>
    /// Moves a desktop icon to the given position using LVM_SETITEMPOSITION.
    /// The icon is identified by matching its filename in the desktop ListView.
    /// </summary>
    private static void SetDesktopIconPosition(string path, int x, int y)
    {
        try
        {
            var listView = GetDesktopListView();
            if (listView == IntPtr.Zero)
            {
                Debug.WriteLine("DesktopIconService: Could not find desktop ListView.");
                return;
            }

            // LVM_SETITEMPOSITION sends MAKELPARAM(x, y) as lParam
            // and the item index as wParam. We need to find the index
            // for the given path. However, reading item text from another
            // process's ListView requires cross-process memory (VirtualAllocEx).
            //
            // For the MVP, we use a simpler approach: the item index matches
            // the order returned by GetDesktopIconPaths (which lists files
            // in the same order the shell enumerates them).
            var fileName = Path.GetFileName(path);
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            // Build a list matching the desktop icon order
            var allFiles = new List<string>();
            if (Directory.Exists(desktopPath))
                allFiles.AddRange(Directory.GetFiles(desktopPath));
            if (Directory.Exists(commonDesktop))
                allFiles.AddRange(Directory.GetFiles(commonDesktop));

            var index = allFiles.FindIndex(f =>
                string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                Debug.WriteLine($"DesktopIconService: Icon not found on desktop: {fileName}");
                return;
            }

            NativeMethods.SendMessageW(
                listView,
                NativeMethods.LVM_SETITEMPOSITION,
                (IntPtr)index,
                NativeMethods.MakeLParam(x, y));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DesktopIconService: SetDesktopIconPosition failed: {ex.Message}");
        }
    }
}
