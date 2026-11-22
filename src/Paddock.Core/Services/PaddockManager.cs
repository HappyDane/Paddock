using Paddock.Core.Models;

namespace Paddock.Core.Services;

public class PaddockManager
{
    private const int IconsPerRow = 4;

    private readonly SettingsService _settingsService;
    private readonly IconStore? _iconStore;
    private readonly ISharedDesktopRemover? _sharedRemover;

    public PaddockManager(SettingsService settingsService)
        : this(settingsService, null)
    {
    }

    /// <summary>
    /// Creates a manager that also moves the physical files behind icons.
    /// Pass <c>null</c> for <paramref name="iconStore"/> to keep the manager
    /// purely in-memory (used by tests and by headless callers).
    /// Without a <paramref name="sharedRemover"/>, items on the all-users
    /// desktop are only referenced (they stay on the desktop as well).
    /// </summary>
    public PaddockManager(
        SettingsService settingsService,
        IconStore? iconStore,
        ISharedDesktopRemover? sharedRemover = null)
    {
        _settingsService = settingsService;
        _iconStore = iconStore;
        _sharedRemover = sharedRemover;
    }

    /// <summary>The attached file store, or <c>null</c> for in-memory use.</summary>
    public IconStore? Store => _iconStore;

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
        _iconStore?.EnsurePaddockFolder(paddock.Id);
        SaveLayout();

        return paddock;
    }

    public PaddockModel? GetPaddock(string id)
    {
        return GetPaddocks().FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// Deletes a paddock. Any items it holds are moved back to the desktop
    /// first, so removing a paddock never loses files.
    /// </summary>
    public bool DeletePaddock(string id)
    {
        var paddocks = GetPaddocks();
        var paddock = paddocks.FirstOrDefault(p => p.Id == id);
        if (paddock is null)
            return false;

        _iconStore?.EvacuatePaddock(id);

        paddocks.Remove(paddock);
        SaveLayout();
        return true;
    }

    /// <summary>
    /// Adds an item to a paddock, moving it off the desktop when an
    /// <see cref="IconStore"/> is attached. Returns the new entry, or the
    /// existing one when the item was already in this paddock.
    /// </summary>
    public IconEntry? AddIconToPaddock(string paddockId, string sourcePath)
    {
        var paddock = GetPaddock(paddockId);
        if (paddock is null || string.IsNullOrWhiteSpace(sourcePath))
            return null;

        var existing = FindEntry(paddock, sourcePath);
        if (existing is not null)
            return existing;

        var path = sourcePath;
        var managed = false;

        if (_iconStore is not null)
        {
            var moved = _iconStore.MoveIntoPaddock(sourcePath, paddockId);
            if (moved is not null)
            {
                path = moved;
                managed = true;
            }
            else if (!IconStore.ItemExists(sourcePath))
            {
                // Stale path (already moved, or deleted behind our back) — adding
                // it would leave a phantom icon pointing at nothing.
                return null;
            }
        }

        // The move may have renamed the item to avoid a collision.
        return AddEntry(paddock, path, managed);
    }

    /// <summary>
    /// Adds several items in one go: one settings write, and — for items on the
    /// desktop shared by all accounts — at most one request to remove the
    /// originals, so a drop of five shortcuts never means five permission
    /// prompts. Returns the entries now in the paddock for these items.
    ///
    /// Shared-desktop files are copied into the paddock first and the original
    /// removed afterwards; if the original cannot be removed (permission
    /// declined, say) the copy is thrown away and the item is referenced where
    /// it is instead, exactly as it was before this existed.
    /// </summary>
    public IReadOnlyList<IconEntry> AddIconsToPaddock(string paddockId, IEnumerable<string> sourcePaths)
    {
        var paddock = GetPaddock(paddockId);
        if (paddock is null)
            return [];

        var results = new List<IconEntry>();
        var sharedCopies = new List<(string Original, string Copy)>();

        using (DeferSave())
        {
            foreach (var source in sourcePaths
                         .Where(p => !string.IsNullOrWhiteSpace(p))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_sharedRemover is not null
                    && _iconStore is not null
                    && _iconStore.IsOnSharedDesktop(source)
                    && FindEntry(paddock, source) is null)
                {
                    var copy = _iconStore.CopyIntoPaddock(source, paddockId);
                    if (copy is not null)
                    {
                        sharedCopies.Add((source, copy));
                        continue;
                    }
                }

                var entry = AddIconToPaddock(paddockId, source);
                if (entry is not null)
                    results.Add(entry);
            }

            if (sharedCopies.Count > 0)
                results.AddRange(FinishSharedCopies(paddock, sharedCopies));
        }

        return results;
    }

    private List<IconEntry> FinishSharedCopies(PaddockModel paddock, List<(string Original, string Copy)> copies)
    {
        IReadOnlyCollection<string> removed;

        try
        {
            removed = _sharedRemover!.Remove(copies.Select(c => c.Original).ToList());
        }
        catch (Exception ex)
        {
            Log.Error("Removing items from the all-users desktop failed.", ex);
            removed = [];
        }

        var gone = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
        var entries = new List<IconEntry>();

        foreach (var (original, copy) in copies)
        {
            if (gone.Contains(original))
            {
                entries.Add(AddEntry(paddock, copy, managed: true));
                continue;
            }

            // Still on the shared desktop: a second, moved copy would only be a
            // confusing duplicate. Reference the original instead.
            _iconStore!.DiscardCopy(copy);
            entries.Add(FindEntry(paddock, original) ?? AddEntry(paddock, original, managed: false));
        }

        if (gone.Count < copies.Count)
        {
            Log.Info($"{copies.Count - gone.Count} all-users desktop item(s) could not be removed " +
                     "there and are referenced in place instead.");
        }

        return entries;
    }

    private IconEntry AddEntry(PaddockModel paddock, string path, bool managed)
    {
        var existing = FindEntry(paddock, path);
        if (existing is not null)
            return existing;

        var entry = new IconEntry
        {
            DesktopPath = path,
            Managed = managed,
            GridPosition = GetNextGridPosition(paddock)
        };

        paddock.Icons.Add(entry);
        SaveLayout();
        return entry;
    }

    /// <summary>
    /// Forgets an icon without touching the file on disk.
    /// Use <see cref="EjectIcon"/> to also put the file back on the desktop.
    /// </summary>
    public void RemoveIconFromPaddock(string paddockId, string path)
    {
        var paddock = GetPaddock(paddockId);
        if (paddock is null)
            return;

        var icon = FindEntry(paddock, path);
        if (icon is not null)
        {
            paddock.Icons.Remove(icon);
            SaveLayout();
        }
    }

    /// <summary>
    /// Takes an item out of a paddock and puts it back on the desktop.
    /// Returns the item's path on the desktop, or <c>null</c> if it was only
    /// referenced (never moved) or is no longer on disk.
    /// </summary>
    public string? EjectIcon(string paddockId, string path)
    {
        var paddock = GetPaddock(paddockId);
        if (paddock is null)
            return null;

        var icon = FindEntry(paddock, path);
        if (icon is null)
            return null;

        string? desktopPath = null;
        if (icon.Managed && _iconStore is not null)
            desktopPath = _iconStore.MoveToDesktop(icon.DesktopPath);

        paddock.Icons.Remove(icon);
        SaveLayout();
        return desktopPath;
    }

    public void MoveIconBetweenPaddocks(string sourcePaddockId, string targetPaddockId, string path)
    {
        var source = GetPaddock(sourcePaddockId);
        var target = GetPaddock(targetPaddockId);
        if (source is null || target is null || source.Id == target.Id)
            return;

        var icon = FindEntry(source, path);
        if (icon is null)
            return;

        // Prevent duplicates in target
        if (FindEntry(target, icon.DesktopPath) is not null)
            return;

        if (icon.Managed && _iconStore is not null)
        {
            var moved = _iconStore.MoveIntoPaddock(icon.DesktopPath, targetPaddockId);
            if (moved is not null)
                icon.DesktopPath = moved;
        }

        source.Icons.Remove(icon);
        icon.GridPosition = GetNextGridPosition(target);
        target.Icons.Add(icon);

        SaveLayout();
    }

    /// <summary>
    /// Brings every paddock back in line with what is actually on disk: drops
    /// entries whose file is gone, and picks up files that appeared in a
    /// paddock's folder while the app was closed (or were dropped there via
    /// Explorer). No-op when the manager has no <see cref="IconStore"/>.
    /// </summary>
    public bool ReconcilePaddocks()
    {
        if (_iconStore is null)
            return false;

        var changed = false;

        foreach (var paddock in GetPaddocks())
        {
            var touched = paddock.Icons.RemoveAll(i => !IconStore.ItemExists(i.DesktopPath)) > 0;

            foreach (var item in _iconStore.ListPaddockItems(paddock.Id))
            {
                if (FindEntry(paddock, item) is not null)
                    continue;

                paddock.Icons.Add(new IconEntry
                {
                    DesktopPath = item,
                    Managed = true,
                    GridPosition = GetNextGridPosition(paddock)
                });
                touched = true;
            }

            if (!touched)
                continue;

            ReassignGridPositions(paddock);
            changed = true;
        }

        if (changed)
            SaveLayout();

        return changed;
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

        paddock.Icons = GetSortedIcons(paddock);
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

    /// <summary>
    /// How many items Paddock has taken off the desktop (moved into a
    /// paddock's folder). Referenced items do not count: they never left.
    /// </summary>
    public int CountMovedIcons() => GetPaddocks().Sum(p => p.Icons.Count(i => i.Managed));

    public void SaveLayout()
    {
        _settingsService.Save();
    }

    /// <summary>
    /// Batches the settings writes of a bulk operation into a single write.
    /// Dispose the returned scope to flush.
    /// </summary>
    public IDisposable DeferSave() => _settingsService.DeferSave();

    private static IconEntry? FindEntry(PaddockModel paddock, string path)
        => paddock.Icons.FirstOrDefault(i =>
            string.Equals(i.DesktopPath, path, StringComparison.OrdinalIgnoreCase));

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
        for (var i = 0; i < paddock.Icons.Count; i++)
        {
            paddock.Icons[i].GridPosition = new GridPosition
            {
                Row = i / IconsPerRow,
                Col = i % IconsPerRow
            };
        }
    }

    private static GridPosition GetNextGridPosition(PaddockModel paddock)
    {
        var count = paddock.Icons.Count;
        return new GridPosition
        {
            Row = count / IconsPerRow,
            Col = count % IconsPerRow
        };
    }
}
