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
/// Items on the desktop shared by all accounts are the one special case: they
/// are <see cref="CopyIntoPaddock">copied</see> in, and the original is removed
/// separately (that needs permission — see <see cref="ISharedDesktopRemover"/>).
///
/// Both roots are injected, so this type performs no Windows-specific calls and
/// is unit-testable against temp directories.
/// </summary>
public class IconStore
{
    /// <summary>
    /// A paddock's own description (title, place, look), kept hidden in its
    /// folder so that the folder alone is enough to rebuild the paddock if
    /// settings.json is ever lost. Never an item, and never overwritten by one.
    /// </summary>
    public const string MetadataName = ".paddock.json";

    private const string MetadataTempName = ".paddock.json.tmp";

    private static readonly string[] IgnoredNames = ["desktop.ini", "thumbs.db", MetadataName, MetadataTempName];

    /// <summary>The note at the top of the store that says what it is and how to get everything back.</summary>
    public const string ReadmeName = "README.txt";

    private const string ReadmeText =
        """
        This folder belongs to Paddock, the desktop organizer.

        The icons you put in a paddock are kept here, one folder per paddock -
        that is what takes them off the desktop. Nothing in here is ever deleted.

        To put every icon back on the desktop:
          - with Paddock running: tray icon > Restore All Icons to Desktop
          - without it:           run   Paddock.exe --restore-all
          - or by hand:           move the files out of these folders onto your desktop

        Whenever Paddock is running, it shows these icons in their paddocks.
        """;

    public IconStore(string desktopRoot, string storeRoot, string? sharedDesktopRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);

        DesktopRoot = Normalize(desktopRoot);
        StoreRoot = Normalize(storeRoot);
        SharedDesktopRoot = string.IsNullOrWhiteSpace(sharedDesktopRoot) ? null : Normalize(sharedDesktopRoot);
    }

    /// <summary>The user's Desktop folder — where ejected items are placed.</summary>
    public string DesktopRoot { get; }

    /// <summary>
    /// The desktop shared by every account (<c>C:\Users\Public\Desktop</c>),
    /// whose items also appear on this user's desktop. <c>null</c> when unknown.
    /// </summary>
    public string? SharedDesktopRoot { get; }

    /// <summary>Parent folder holding one subfolder per paddock.</summary>
    public string StoreRoot { get; }

    /// <summary>
    /// Raised with the new path whenever the store puts an item back on the
    /// desktop — so the auto-organize rules can tell an item Paddock returned
    /// from one that just arrived, and leave it where it is.
    /// </summary>
    public event Action<string>? PlacedOnDesktop;

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

    /// <summary>
    /// True when the path is a file directly on the all-users desktop — the
    /// kind of item that can be copied into a paddock and then removed there.
    /// Folders are left alone: copying a whole tree to hide one icon is not a
    /// trade worth making silently.
    /// </summary>
    public bool IsOnSharedDesktop(string path)
    {
        if (SharedDesktopRoot is null)
            return false;

        var parent = GetParentDirectory(path);
        return parent is not null && PathEquals(parent, SharedDesktopRoot) && File.Exists(path);
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
    /// Copies a file into the given paddock's folder, leaving the original
    /// where it is. Returns the copy's path, or <c>null</c> on failure.
    /// </summary>
    public string? CopyIntoPaddock(string sourcePath, string paddockId)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(paddockId))
            return null;

        var source = Normalize(sourcePath);
        if (!File.Exists(source))
            return null;

        try
        {
            var folder = EnsurePaddockFolder(paddockId);
            var target = GetAvailablePath(folder, Path.GetFileName(source));
            File.Copy(source, target);
            return target;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not copy '{source}' into paddock '{paddockId}'.", ex);
            return null;
        }
    }

    /// <summary>
    /// Deletes a copy made by <see cref="CopyIntoPaddock"/> that turned out not
    /// to be needed. Refuses anything outside the store, so it can never touch
    /// a user's original.
    /// </summary>
    public void DiscardCopy(string copyPath)
    {
        if (!IsInStore(copyPath))
            return;

        try
        {
            if (File.Exists(copyPath))
                File.Delete(copyPath);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not remove the unneeded copy '{copyPath}': {ex.Message}");
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
            PlacedOnDesktop?.Invoke(target);
            return target;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not move '{source}' back to the desktop.", ex);
            return null;
        }
    }

    /// <summary>
    /// Writes the store's README (what this folder is, how to get the icons
    /// back without Paddock) unless it is already there.
    /// </summary>
    public void EnsureReadme()
    {
        try
        {
            Directory.CreateDirectory(StoreRoot);
            var path = Path.Combine(StoreRoot, ReadmeName);
            if (!File.Exists(path))
                File.WriteAllText(path, ReadmeText.ReplaceLineEndings("\r\n"));
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not write the note in '{StoreRoot}': {ex.Message}");
        }
    }

    /// <summary>Writes a paddock's description into its folder (atomically, hidden).</summary>
    public void WriteMetadata(string paddockId, string json)
    {
        try
        {
            var folder = EnsurePaddockFolder(paddockId);
            var path = Path.Combine(folder, MetadataName);
            var temp = Path.Combine(folder, MetadataTempName);

            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not write the description of paddock '{paddockId}': {ex.Message}");
        }
    }

    /// <summary>A paddock's description from its folder, or null when there is none.</summary>
    public string? ReadMetadata(string paddockId)
    {
        try
        {
            var path = Path.Combine(GetPaddockFolder(paddockId), MetadataName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the description of paddock '{paddockId}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Removes a paddock's description: it is being deleted, and must not
    /// come back from its folder if the folder cannot be removed.
    /// </summary>
    public void DeleteMetadata(string paddockId)
    {
        try
        {
            var path = Path.Combine(GetPaddockFolder(paddockId), MetadataName);
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not remove the description of paddock '{paddockId}': {ex.Message}");
        }
    }

    /// <summary>The names (paddock ids) of the folders in the store.</summary>
    public IReadOnlyList<string> ListPaddockFolders()
    {
        if (!Directory.Exists(StoreRoot))
            return [];

        try
        {
            return Directory.EnumerateDirectories(StoreRoot).Select(Path.GetFileName).OfType<string>().ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not list the paddock folders: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Items sitting loose at the top of the store, in no paddock's folder
    /// (dropped there by hand, say) — nothing would ever show them.
    /// </summary>
    public IReadOnlyList<string> ListStrayItems()
    {
        if (!Directory.Exists(StoreRoot))
            return [];

        try
        {
            return Directory.EnumerateFiles(StoreRoot)
                .Where(p => !IsIgnored(p) && !string.Equals(Path.GetFileName(p), ReadmeName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not list the paddock store: {ex.Message}");
            return [];
        }
    }

    /// <summary>Removes a paddock folder that has nothing in it. Never removes one that does.</summary>
    public void RemoveFolderIfEmpty(string paddockId)
    {
        var folder = GetPaddockFolder(paddockId);

        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not remove the empty folder '{folder}': {ex.Message}");
        }
    }

    /// <summary>
    /// Moves every item in the store — in any paddock's folder, or loose —
    /// back onto the desktop, whether or not Paddock knows about it. The
    /// folders stay. For <c>Paddock.exe --restore-all</c>: it needs neither
    /// settings.json nor a running Paddock. Returns how many items moved.
    /// </summary>
    public int RestoreEverything()
    {
        var moved = 0;

        foreach (var id in ListPaddockFolders())
        {
            foreach (var item in ListPaddockItems(id))
            {
                if (MoveToDesktop(item) is not null)
                    moved++;
            }
        }

        foreach (var stray in ListStrayItems())
        {
            if (MoveToDesktop(stray) is not null)
                moved++;
        }

        return moved;
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
        // First, so that a folder left behind (an item that would not move)
        // comes back as "Recovered", never as the paddock that was deleted.
        DeleteMetadata(paddockId);

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

    /// <summary>Names Paddock keeps for itself inside a paddock folder; an item never gets one.</summary>
    private static bool IsReserved(string fileName)
        => string.Equals(fileName, MetadataName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(fileName, MetadataTempName, StringComparison.OrdinalIgnoreCase);

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
        if (!ItemExists(candidate) && !IsReserved(fileName))
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
