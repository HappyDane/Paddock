using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Paddock.Shell;

/// <summary>
/// Extracts file icons using the Windows Shell and caches them by file
/// extension (for generic file types) or full path (for executables, shortcuts
/// and folders, whose icons are unique per item).
///
/// Icons come from the shell's 48×48 "extra large" image list rather than the
/// 32×32 default, so they stay crisp at the size paddocks draw them.
/// </summary>
public sealed class IconExtractor
{
    private static readonly HashSet<string> PerPathExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".lnk",
        ".url",
        ".appref-ms",
    };

    private readonly ConcurrentDictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a cached or freshly extracted icon for the given path.
    /// </summary>
    public BitmapSource? GetIcon(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var cacheKey = GetCacheKey(filePath);

        return _cache.GetOrAdd(cacheKey, _ => ExtractIcon(filePath));
    }

    /// <summary>
    /// Drops a single item from the cache — used when a file is renamed or
    /// replaced and its icon may have changed.
    /// </summary>
    public void Invalidate(string filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
            _cache.TryRemove(GetCacheKey(filePath), out _);
    }

    /// <summary>
    /// Removes all cached icons, allowing them to be re-extracted on next access.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    private static string GetCacheKey(string filePath)
    {
        // Folders and self-iconed file types get their own entry; everything
        // else shares one icon per extension.
        if (Directory.Exists(filePath))
            return filePath;

        var extension = Path.GetExtension(filePath);

        if (PerPathExtensions.Contains(extension))
            return filePath;

        return string.IsNullOrEmpty(extension) ? filePath : extension;
    }

    private static BitmapSource? ExtractIcon(string filePath)
    {
        return ExtractFromSystemImageList(filePath, NativeMethods.SHIL_EXTRALARGE)
            ?? ExtractSmallIcon(filePath);
    }

    /// <summary>
    /// Pulls the item's icon out of one of the shell's system image lists,
    /// which is the only way to get sizes above 32×32.
    /// </summary>
    private static BitmapSource? ExtractFromSystemImageList(string filePath, int imageListSize)
    {
        NativeMethods.IImageList? imageList = null;
        var iconHandle = IntPtr.Zero;

        try
        {
            var shellInfo = new NativeMethods.SHFILEINFO();
            var flags = NativeMethods.SHGFI_SYSICONINDEX;
            var attributes = 0u;

            if (!ItemExists(filePath))
            {
                // Unknown/missing item: still show the icon for its type.
                flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
                attributes = NativeMethods.FILE_ATTRIBUTE_NORMAL;
            }

            var result = NativeMethods.SHGetFileInfo(
                filePath,
                attributes,
                ref shellInfo,
                (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                flags);

            if (result == IntPtr.Zero)
                return null;

            var iid = NativeMethods.IID_IImageList;
            if (NativeMethods.SHGetImageList(imageListSize, ref iid, out imageList) != 0 || imageList is null)
                return null;

            if (imageList.GetIcon(shellInfo.iIcon, NativeMethods.ILD_TRANSPARENT, out iconHandle) != 0
                || iconHandle == IntPtr.Zero)
            {
                return null;
            }

            return CreateBitmap(iconHandle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"IconExtractor: system image list failed for '{filePath}': {ex.Message}");
            return null;
        }
        finally
        {
            if (iconHandle != IntPtr.Zero)
                NativeMethods.DestroyIcon(iconHandle);

            if (imageList is not null)
                Marshal.ReleaseComObject(imageList);
        }
    }

    /// <summary>Fallback path: the classic 32×32 shell icon.</summary>
    private static BitmapSource? ExtractSmallIcon(string filePath)
    {
        var shellInfo = new NativeMethods.SHFILEINFO();
        var flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON;
        var attributes = 0u;

        if (!ItemExists(filePath))
        {
            flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
            attributes = NativeMethods.FILE_ATTRIBUTE_NORMAL;
        }

        var result = NativeMethods.SHGetFileInfo(
            filePath,
            attributes,
            ref shellInfo,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            flags);

        if (result == IntPtr.Zero || shellInfo.hIcon == IntPtr.Zero)
        {
            Debug.WriteLine($"IconExtractor: SHGetFileInfo failed for '{filePath}'.");
            return null;
        }

        try
        {
            return CreateBitmap(shellInfo.hIcon);
        }
        finally
        {
            NativeMethods.DestroyIcon(shellInfo.hIcon);
        }
    }

    private static BitmapSource? CreateBitmap(IntPtr iconHandle)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // Freeze so the same instance can be shared across threads.
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"IconExtractor: failed to create BitmapSource: {ex.Message}");
            return null;
        }
    }

    private static bool ItemExists(string path)
        => File.Exists(path) || Directory.Exists(path);
}
