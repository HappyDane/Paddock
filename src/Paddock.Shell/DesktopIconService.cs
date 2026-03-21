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
        // TODO: Use Shell32 COM to hide the icon from the desktop ListView
        // This requires sending LVM_SETITEMPOSITION to the desktop ListView
        // with coordinates off-screen (e.g., -10000, -10000)
    }

    /// <summary>
    /// Restores a desktop icon to a visible position.
    /// Used when an icon is removed from a paddock.
    /// </summary>
    public void ShowDesktopIcon(string path, int x, int y)
    {
        // TODO: Use Shell32 COM to set the icon position in the desktop ListView
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
}
