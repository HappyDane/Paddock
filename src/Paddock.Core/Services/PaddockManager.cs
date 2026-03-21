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
        var paddock = new PaddockModel
        {
            Title = title,
            X = x,
            Y = y,
            Width = width,
            Height = height
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

        // Don't add duplicates
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

    public void SaveLayout()
    {
        _settingsService.Save();
    }

    private static GridPosition GetNextGridPosition(PaddockModel paddock)
    {
        if (paddock.Icons.Count == 0)
            return new GridPosition { Row = 0, Col = 0 };

        // Simple auto-arrange: fill columns first, then rows
        // Assume ~4 icons per row based on default paddock width
        const int iconsPerRow = 4;
        var count = paddock.Icons.Count;
        return new GridPosition
        {
            Row = count / iconsPerRow,
            Col = count % iconsPerRow
        };
    }
}
