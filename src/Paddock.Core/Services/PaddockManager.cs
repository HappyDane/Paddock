using Paddock.Core.Models;

namespace Paddock.Core.Services;

public class PaddockManager
{
    private readonly SettingsService _settingsService;

    public PaddockManager(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public List<PaddockModel> GetPaddocks()
    {
        return _settingsService.GetActiveCorral().Paddocks;
    }

    public PaddockModel CreatePaddock(string title, double x, double y, double width, double height)
    {
        var settings = _settingsService.Settings;
        var paddock = new PaddockModel
        {
            Title = title,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Style = new PaddockStyle
            {
                Opacity = settings.DefaultOpacity,
                CornerRadius = settings.DefaultCornerRadius
            }
        };

        _settingsService.GetActiveCorral().Paddocks.Add(paddock);
        SaveLayout();

        return paddock;
    }

    public PaddockModel? GetPaddock(string id)
    {
        return GetPaddocks().FirstOrDefault(p => p.Id == id);
    }

    public bool DeletePaddock(string id)
    {
        var paddocks = GetPaddocks();
        var paddock = paddocks.FirstOrDefault(p => p.Id == id);
        if (paddock is null)
            return false;

        paddocks.Remove(paddock);
        SaveLayout();
        return true;
    }

    public void AddIconToPaddock(string paddockId, string desktopPath)
    {
        var paddock = GetPaddock(paddockId);
        if (paddock is null)
            return;

        if (paddock.Icons.Any(i => i.DesktopPath == desktopPath))
            return;

        var nextPosition = GetNextGridPosition(paddock);
        paddock.Icons.Add(new IconEntry
        {
            DesktopPath = desktopPath,
            GridPosition = nextPosition
        });

        SaveLayout();
    }

    public void RemoveIconFromPaddock(string paddockId, string desktopPath)
    {
        var paddock = GetPaddock(paddockId);
        if (paddock is null)
            return;

        var icon = paddock.Icons.FirstOrDefault(i => i.DesktopPath == desktopPath);
        if (icon is not null)
        {
            paddock.Icons.Remove(icon);
            SaveLayout();
        }
    }

    public void MoveIconBetweenPaddocks(string sourcePaddockId, string targetPaddockId, string desktopPath)
    {
        var source = GetPaddock(sourcePaddockId);
        var target = GetPaddock(targetPaddockId);
        if (source is null || target is null)
            return;

        var icon = source.Icons.FirstOrDefault(i => i.DesktopPath == desktopPath);
        if (icon is null)
            return;

        // Prevent duplicates in target
        if (target.Icons.Any(i => i.DesktopPath == desktopPath))
            return;

        source.Icons.Remove(icon);
        icon.GridPosition = GetNextGridPosition(target);
        target.Icons.Add(icon);

        SaveLayout();
    }

    /// <summary>
    /// Sort icons within a paddock by the specified field and direction.
    /// </summary>
    public void SortPaddockIcons(string paddockId, SortField sortBy, bool ascending)
    {
        var paddock = GetPaddock(paddockId);
        if (paddock is null)
            return;

        paddock.SortBy = sortBy;
        paddock.SortAscending = ascending;

        var sorted = sortBy switch
        {
            SortField.Name => ascending
                ? paddock.Icons.OrderBy(i => Path.GetFileNameWithoutExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList()
                : paddock.Icons.OrderByDescending(i => Path.GetFileNameWithoutExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList(),

            SortField.FileType => ascending
                ? paddock.Icons.OrderBy(i => Path.GetExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList()
                : paddock.Icons.OrderByDescending(i => Path.GetExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList(),

            SortField.DateModified => SortByFileDate(paddock.Icons, ascending),

            SortField.FileSize => SortByFileSize(paddock.Icons, ascending),

            _ => paddock.Icons
        };

        // Reassign grid positions after sort
        paddock.Icons = sorted;
        ReassignGridPositions(paddock);
        SaveLayout();
    }

    /// <summary>
    /// Get the sorted icons list for a paddock without modifying the model.
    /// </summary>
    public static List<IconEntry> GetSortedIcons(PaddockModel paddock)
    {
        return paddock.SortBy switch
        {
            SortField.Name => paddock.SortAscending
                ? paddock.Icons.OrderBy(i => Path.GetFileNameWithoutExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList()
                : paddock.Icons.OrderByDescending(i => Path.GetFileNameWithoutExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList(),

            SortField.FileType => paddock.SortAscending
                ? paddock.Icons.OrderBy(i => Path.GetExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList()
                : paddock.Icons.OrderByDescending(i => Path.GetExtension(i.DesktopPath), StringComparer.OrdinalIgnoreCase).ToList(),

            SortField.DateModified => SortByFileDate(paddock.Icons, paddock.SortAscending),
            SortField.FileSize => SortByFileSize(paddock.Icons, paddock.SortAscending),
            _ => paddock.Icons.ToList()
        };
    }

    public void SaveLayout()
    {
        _settingsService.Save();
    }

    private static List<IconEntry> SortByFileDate(List<IconEntry> icons, bool ascending)
    {
        return ascending
            ? icons.OrderBy(i => GetFileDate(i.DesktopPath)).ToList()
            : icons.OrderByDescending(i => GetFileDate(i.DesktopPath)).ToList();
    }

    private static List<IconEntry> SortByFileSize(List<IconEntry> icons, bool ascending)
    {
        return ascending
            ? icons.OrderBy(i => GetFileSize(i.DesktopPath)).ToList()
            : icons.OrderByDescending(i => GetFileSize(i.DesktopPath)).ToList();
    }

    private static DateTime GetFileDate(string path)
    {
        try { return File.GetLastWriteTime(path); }
        catch { return DateTime.MinValue; }
    }

    private static long GetFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static void ReassignGridPositions(PaddockModel paddock)
    {
        const int iconsPerRow = 4;
        for (var i = 0; i < paddock.Icons.Count; i++)
        {
            paddock.Icons[i].GridPosition = new GridPosition
            {
                Row = i / iconsPerRow,
                Col = i % iconsPerRow
            };
        }
    }

    private static GridPosition GetNextGridPosition(PaddockModel paddock)
    {
        if (paddock.Icons.Count == 0)
            return new GridPosition { Row = 0, Col = 0 };

        const int iconsPerRow = 4;
        var count = paddock.Icons.Count;
        return new GridPosition
        {
            Row = count / iconsPerRow,
            Col = count % iconsPerRow
        };
    }
}
