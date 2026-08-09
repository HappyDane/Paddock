namespace Paddock.Core.Models;

public class IconEntry
{
    /// <summary>
    /// Full path of the item as it currently lives on disk. For managed
    /// entries this points inside the paddock's own folder, not the desktop.
    /// The name is kept for settings.json backwards compatibility.
    /// </summary>
    public string DesktopPath { get; set; } = string.Empty;

    /// <summary>
    /// True when Paddock physically moved the item into the paddock's folder,
    /// which is what removes it from the desktop. False for items that are only
    /// referenced where they are (e.g. dragged in from a folder in Explorer).
    /// </summary>
    public bool Managed { get; set; }

    public GridPosition GridPosition { get; set; } = new();
}

public class GridPosition
{
    public int Row { get; set; }
    public int Col { get; set; }
}
