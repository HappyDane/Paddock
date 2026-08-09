namespace Paddock.Core.Services;

/// <summary>
/// Owns the files behind a paddock's icons.
///
/// Every paddock is backed by a real folder underneath <see cref="StoreRoot"/>.
/// Dropping a desktop item into a paddock physically moves the file into that
/// folder, and that is what makes the icon disappear from the desktop — the
/// shell only draws the <em>top level</em> entries of the Desktop folder.
/// Ejecting an item moves it straight back out to the desktop, so the user's
/// files are never hidden somewhere they cannot find them.
///
/// Only items that live on the desktop (or already inside another paddock) are
/// ever moved. Anything dragged in from elsewhere is referenced where it is.
///
/// Both roots are injected, so this type performs no Windows-specific calls and
/// is unit-testable against temp directories.
/// </summary>
public class IconStore
{
    private static readonly string[] IgnoredNames = ["desktop.ini", "thumbs.db"];

    public IconStore(string desktopRoot, string storeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);

        DesktopRoot = Normalize(desktopRoot);
        StoreRoot = Normalize(storeRoot);
    }

    /// <summary>The user's Desktop folder — where ejected items are placed.</summary>
    public string DesktopRoot { get; }

    /// <summary>Parent folder holding one subfolder per paddock.</summary>
    public string StoreRoot { get; }

    public string GetPaddockFolder(string paddockId)
        => Path.Combine(StoreRoot, paddockId);

    public string EnsurePaddockFolder(string paddockId)
    {
        var folder = GetPaddockFolder(paddockId);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>True when the path is a direct child of the desktop folder.</summary>
    public bool IsOnDesktop(string path)
    {
        var parent = GetParentDirectory(path);
        return parent is not null && PathEquals(parent, DesktopRoot);
    }

    /// <summary>True when the path lives inside any paddock folder.</summary>
    public bool IsInStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var full = Normalize(path);
        return full.StartsWith(StoreRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Moves an item into the given paddock's folder.
    /// Returns the item's new path, or <c>null</c> when it was left in place
    /// (not a desktop item, missing, or the move failed).
    /// </summary>
    public string? MoveIntoPaddock(string sourcePath, string paddockId)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(paddockId))
            return null;

        var source = Normalize(sourcePath);
        var folder = Normalize(GetPaddockFolder(paddockId));

        // Already sitting in this paddock's folder — nothing to do.
        var parent = GetParentDirectory(source);
        if (parent is not null && PathEquals(parent, folder))
            return source;

        // Never relocate files from outside the desktop.
        if (!IsOnDesktop(source) && !IsInStore(source))
            return null;

        if (!ItemExists(source))
            return null;

        try
        {
            Directory.CreateDirectory(folder);
            var target = GetAvailablePath(folder, Path.GetFileName(source));
            MoveItem(source, target);
            return target;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not move '{source}' into paddock '{paddockId}'.", ex);
            return null;
        }
    }

    /// <summary>
    /// Moves an item back out to the desktop.
    /// Returns its new path, or <c>null</c> when nothing was moved.
    /// </summary>
    public string? MoveToDesktop(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var source = Normalize(path);

        if (!ItemExists(source))
            return null;

        if (IsOnDesktop(source))
            return source;

        try
        {
            Directory.CreateDirectory(DesktopRoot);
            var target = GetAvailablePath(DesktopRoot, Path.GetFileName(source));
            MoveItem(source, target);
            return target;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not move '{source}' back to the desktop.", ex);
            return null;
        }
    }

    /// <summary>Lists the items physically present in a paddock's folder.</summary>
    public IReadOnlyList<string> ListPaddockItems(string paddockId)
    {
        var folder = GetPaddockFolder(paddockId);
        if (!Directory.Exists(folder))
            return [];

        try
        {
            return Directory.EnumerateFileSystemEntries(folder)
                .Where(p => !IsIgnored(p))
                .Select(Normalize)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not list paddock '{paddockId}': {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Moves everything out of a paddock's folder back to the desktop and
    /// removes the folder. Used when a paddock is deleted, so no file is ever
    /// stranded inside a paddock that no longer exists.
    /// </summary>
    public void EvacuatePaddock(string paddockId)
    {
        foreach (var item in ListPaddockItems(paddockId))
        {
            MoveToDesktop(item);
        }

        var folder = GetPaddockFolder(paddockId);

        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not remove the folder for paddock '{paddockId}': {ex.Message}");
        }
    }

    /// <summary>True when the item still exists on disk.</summary>
    public static bool ItemExists(string path)
        => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));

    private static bool IsIgnored(string path)
    {
        var name = Path.GetFileName(path);
        return IgnoredNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static void MoveItem(string source, string target)
    {
        if (Directory.Exists(source))
            Directory.Move(source, target);
        else
            File.Move(source, target);
    }

    /// <summary>
    /// Returns a free path in <paramref name="folder"/> for the given file
    /// name, appending " (2)", " (3)" … rather than ever overwriting.
    /// </summary>
    private static string GetAvailablePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!ItemExists(candidate))
            return candidate;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var i = 2; i < 1000; i++)
        {
            candidate = Path.Combine(folder, $"{stem} ({i}){extension}");
            if (!ItemExists(candidate))
                return candidate;
        }

        return Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){extension}");
    }

    private static string? GetParentDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetDirectoryName(Normalize(path));
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot resolve the parent of '{path}': {ex.Message}");
            return null;
        }
    }

    private static bool PathEquals(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
