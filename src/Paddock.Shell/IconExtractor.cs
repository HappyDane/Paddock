using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Paddock.Shell;

/// <summary>
/// Extracts file icons using the Windows Shell32 API and caches them by
/// file extension (for generic file types) or full path (for executables
/// and shortcuts whose icons are unique per file).
/// </summary>
public sealed class IconExtractor
{
    private static readonly HashSet<string> PerPathExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".lnk",
    };

    private readonly ConcurrentDictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a cached or freshly extracted icon for the given file path.
    /// For .exe and .lnk files the cache key is the full path (each may
    /// have its own icon). For all other extensions the key is the extension
    /// itself, because Shell32 returns the same icon for every file of that type.
    /// </summary>
    public BitmapSource? GetIcon(string filePath)
    {
        string cacheKey = GetCacheKey(filePath);

        return _cache.GetOrAdd(cacheKey, _ => ExtractIcon(filePath));
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
        string extension = Path.GetExtension(filePath);

        if (PerPathExtensions.Contains(extension))
        {
            return filePath;
        }

        return string.IsNullOrEmpty(extension) ? filePath : extension;
    }

    private static BitmapSource? ExtractIcon(string filePath)
    {
        // Use SHGFI_USEFILEATTRIBUTES when the file does not exist on disk
        // so we can still get the generic icon for its extension.
        bool fileExists = File.Exists(filePath);
        uint flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON;

        if (!fileExists)
        {
            flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
        }

        var shInfo = new NativeMethods.SHFILEINFO();

        IntPtr result = NativeMethods.SHGetFileInfo(
            filePath,
            0,
            ref shInfo,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            flags);

        if (result == IntPtr.Zero || shInfo.hIcon == IntPtr.Zero)
        {
            Debug.WriteLine($"IconExtractor: SHGetFileInfo failed for '{filePath}'.");
            return null;
        }

        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                shInfo.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // Freeze the bitmap so it can be used across threads.
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"IconExtractor: Failed to create BitmapSource for '{filePath}': {ex.Message}");
            return null;
        }
        finally
        {
            NativeMethods.DestroyIcon(shInfo.hIcon);
        }
    }
}
