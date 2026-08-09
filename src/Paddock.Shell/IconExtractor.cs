using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Extracts file icons using the Windows Shell and caches them by file
/// extension (for generic file types) or full path (for executables, shortcuts
/// and folders, whose icons are unique per item).
///
/// Icons come from the shell's 48×48 "extra large" image list rather than the
/// 32×32 default, so they stay crisp at the size paddocks draw them.
///
/// Extraction is <em>not</em> cheap: resolving a shortcut can touch the disk or
/// even the network, and shell extensions run in-process. So anything not
/// already cached is loaded on a dedicated STA worker thread via
/// <see cref="RequestIcon"/> — a paddock full of shortcuts must never freeze the
/// UI while it opens.
/// </summary>
public sealed class IconExtractor : IDisposable
{
    private static readonly HashSet<string> PerPathExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".lnk",
        ".url",
        ".appref-ms",
    };

    private readonly ConcurrentDictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<PendingRequest> _queue = new();
    private readonly object _workerGate = new();

    private Thread? _worker;
    private bool _disposed;

    /// <summary>
    /// Returns the icon for a path, extracting it on the calling thread if it is
    /// not cached yet. Prefer <see cref="RequestIcon"/> from the UI thread.
    /// </summary>
    public BitmapSource? GetIcon(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        return _cache.GetOrAdd(GetCacheKey(filePath), _ => ExtractIcon(filePath));
    }

    /// <summary>
    /// Reports an already-known icon without touching the shell. The icon may
    /// legitimately be <c>null</c> (extraction failed before), which is why the
    /// result and the "is known" answer are separate.
    /// </summary>
    public bool TryGetCached(string filePath, out BitmapSource? icon)
    {
        icon = null;

        return !string.IsNullOrWhiteSpace(filePath)
               && _cache.TryGetValue(GetCacheKey(filePath), out icon);
    }

    /// <summary>
    /// Queues an icon for background extraction. <paramref name="onLoaded"/> is
    /// invoked on the worker thread with a frozen bitmap (safe to hand to the
    /// UI thread) or <c>null</c> if the icon could not be read.
    /// </summary>
    public void RequestIcon(string filePath, Action<BitmapSource?> onLoaded)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            onLoaded(null);
            return;
        }

        if (TryGetCached(filePath, out var cached))
        {
            onLoaded(cached);
            return;
        }

        EnsureWorker();

        try
        {
            _queue.Add(new PendingRequest(filePath, onLoaded));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Shutting down — nothing left to draw into.
            onLoaded(null);
        }
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // Already gone.
        }
    }

    private void EnsureWorker()
    {
        lock (_workerGate)
        {
            if (_worker is not null || _disposed)
                return;

            var worker = new Thread(ProcessQueue)
            {
                IsBackground = true,
                Name = "Paddock icon loader",
            };

            // Shell extensions expect an initialised apartment; STA is what
            // Explorer itself uses to ask for icons.
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();

            _worker = worker;
        }
    }

    private void ProcessQueue()
    {
        try
        {
            foreach (var request in _queue.GetConsumingEnumerable())
            {
                BitmapSource? icon = null;

                try
                {
                    icon = GetIcon(request.Path);
                }
                catch (Exception ex)
                {
                    Log.Error($"Icon extraction failed for '{request.Path}'.", ex);
                }

                try
                {
                    request.OnLoaded(icon);
                }
                catch (Exception ex)
                {
                    Log.Error($"Icon callback failed for '{request.Path}'.", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Icon loader thread stopped unexpectedly.", ex);
        }
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
            Log.Warn($"System image list unavailable for '{filePath}': {ex.Message}");
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
            Log.Warn($"No shell icon for '{filePath}'.");
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

            // Freeze so the bitmap can cross from the loader thread to the UI.
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to turn an icon handle into a bitmap.", ex);
            return null;
        }
    }

    private static bool ItemExists(string path)
        => File.Exists(path) || Directory.Exists(path);

    private sealed record PendingRequest(string Path, Action<BitmapSource?> OnLoaded);
}
