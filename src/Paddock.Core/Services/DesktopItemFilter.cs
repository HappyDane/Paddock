namespace Paddock.Core.Services;

/// <summary>
/// Broad kinds of desktop item, used when filling a paddock from the desktop.
/// </summary>
public enum DesktopItemCategory
{
    All,
    Shortcuts,
    Folders,
    Documents,
    Images,
    Media,
    Archives,

    /// <summary>Anything that is not a folder and fits none of the named kinds.</summary>
    Other,
}

/// <summary>
/// Sorts desktop items into the categories above, so "put my documents in this
/// paddock" is one click instead of a hunt through a cluttered desktop.
/// </summary>
public static class DesktopItemFilter
{
    private static readonly HashSet<string> Shortcuts = Build(".lnk", ".url", ".appref-ms");

    private static readonly HashSet<string> Documents = Build(
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf",
        ".odt", ".ods", ".odp", ".csv", ".md", ".epub", ".one");

    private static readonly HashSet<string> Images = Build(
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico",
        ".tif", ".tiff", ".heic", ".raw", ".psd");

    private static readonly HashSet<string> Media = Build(
        ".mp3", ".mp4", ".mkv", ".avi", ".mov", ".wav", ".flac", ".m4a",
        ".m4v", ".webm", ".wmv", ".aac", ".ogg", ".opus");

    private static readonly HashSet<string> Archives = Build(
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab");

    public static bool Matches(string path, DesktopItemCategory category)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (category == DesktopItemCategory.All)
            return true;

        var isFolder = Directory.Exists(path);

        if (category == DesktopItemCategory.Folders)
            return isFolder;

        if (isFolder)
            return false;

        var extension = Path.GetExtension(path);

        return category switch
        {
            DesktopItemCategory.Shortcuts => Shortcuts.Contains(extension),
            DesktopItemCategory.Documents => Documents.Contains(extension),
            DesktopItemCategory.Images => Images.Contains(extension),
            DesktopItemCategory.Media => Media.Contains(extension),
            DesktopItemCategory.Archives => Archives.Contains(extension),
            DesktopItemCategory.Other => !Shortcuts.Contains(extension)
                                         && !Documents.Contains(extension)
                                         && !Images.Contains(extension)
                                         && !Media.Contains(extension)
                                         && !Archives.Contains(extension),
            _ => false,
        };
    }

    public static IReadOnlyList<string> Filter(IEnumerable<string> paths, DesktopItemCategory category)
        => paths.Where(p => Matches(p, category)).ToList();

    /// <summary>Human-readable name for menus and confirmation prompts.</summary>
    public static string Describe(DesktopItemCategory category) => category switch
    {
        DesktopItemCategory.All => "items",
        DesktopItemCategory.Shortcuts => "shortcuts",
        DesktopItemCategory.Folders => "folders",
        DesktopItemCategory.Documents => "documents",
        DesktopItemCategory.Images => "images",
        DesktopItemCategory.Media => "media files",
        DesktopItemCategory.Archives => "archives",
        DesktopItemCategory.Other => "other files",
        _ => "items",
    };

    private static HashSet<string> Build(params string[] extensions)
        => new(extensions, StringComparer.OrdinalIgnoreCase);
}
