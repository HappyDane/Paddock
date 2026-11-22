using Paddock.Core.Models;

namespace Paddock.Core.Services;

/// <summary>Where one desktop icon is drawn: the item's file path and the centre of its icon.</summary>
/// <param name="Path">The item's file-system path (user or all-users desktop).</param>
/// <param name="CentreX">Centre of the icon on screen, in the same units as the area it is tested against.</param>
/// <param name="CentreY">See <paramref name="CentreX"/>.</param>
public readonly record struct DesktopIconSpot(string Path, double CentreX, double CentreY);

/// <summary>
/// Decisions about where desktop icons sit, kept free of Windows so they can
/// be tested. The shell layer reads the positions from Explorer.
/// </summary>
public static class DesktopLayout
{
    /// <summary>
    /// The desktop items whose icon is centred inside <paramref name="area"/> —
    /// the ones a paddock drawn over them should take in, as Fences does.
    ///
    /// Paddock's own store folder is never among them: with hidden files shown
    /// it is an icon on the desktop too, and moving it into one of its own
    /// subfolders would be nonsense.
    /// </summary>
    public static IReadOnlyList<string> ItemsUnder(
        IEnumerable<DesktopIconSpot> icons,
        LayoutBounds area,
        IconStore? store = null)
    {
        return icons
            .Where(icon => !string.IsNullOrWhiteSpace(icon.Path))
            .Where(icon => area.Contains(icon.CentreX, icon.CentreY))
            .Where(icon => store is null || !IsPaddocksOwn(icon.Path, store))
            .Select(icon => icon.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPaddocksOwn(string path, IconStore store)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(full, store.StoreRoot, StringComparison.OrdinalIgnoreCase) || store.IsInStore(full);
        }
        catch (Exception)
        {
            // A path we cannot even normalise is not one to move.
            return true;
        }
    }
}
