namespace Paddock.Core.Services;

/// <summary>
/// Removes items from the desktop shared by every account on the machine
/// (<c>C:\Users\Public\Desktop</c>) once Paddock has copied them into a paddock.
///
/// That folder is where most installers put their shortcuts, so a paddock that
/// cannot take them cannot organise much of a real desktop. It is also
/// read-only for normal users, which is why this is a separate, injectable step:
/// the Windows implementation asks for administrator permission when it has to,
/// and tests substitute a fake.
/// </summary>
public interface ISharedDesktopRemover
{
    /// <summary>
    /// Removes as many of <paramref name="paths"/> as it can, in one go — at
    /// most one permission prompt for the whole batch.
    /// </summary>
    /// <returns>The paths that are gone afterwards.</returns>
    IReadOnlyCollection<string> Remove(IReadOnlyList<string> paths);
}
