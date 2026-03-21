namespace Paddock.Core.Models;

public class IconEntry
{
    public string DesktopPath { get; set; } = string.Empty;
    public GridPosition GridPosition { get; set; } = new();
}

public class GridPosition
{
    public int Row { get; set; }
    public int Col { get; set; }
}
