using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Gathers what the auto-organize rules want to know about a desktop item:
/// whether it is a folder, where a shortcut points, and whether Windows marked
/// it as downloaded from the internet. Reading a shortcut uses COM, so call
/// this off the UI thread.
/// </summary>
public static class DesktopItemInspector
{
    public static DesktopItemFacts Inspect(string path, string? cameFromFolder = null)
    {
        var isFolder = Directory.Exists(path);
        string? target = null;
        string? arguments = null;

        if (!isFolder)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".lnk":
                    (target, arguments) = ReadShortcut(path);
                    break;

                case ".url":
                    target = ReadInternetShortcut(path);
                    break;
            }
        }

        return new DesktopItemFacts(path)
        {
            IsFolder = isFolder,
            ShortcutTarget = target,
            ShortcutArguments = arguments,
            DownloadedFromInternet = !isFolder && IsFromTheInternet(path),
            CameFromFolder = cameFromFolder,
        };
    }

    /// <summary>The program a .lnk starts and its arguments — read, never resolved.</summary>
    private static (string? Target, string? Arguments) ReadShortcut(string path)
    {
        object? link = null;

        try
        {
            var type = Type.GetTypeFromCLSID(NativeMethods.CLSID_ShellLink, throwOnError: true)!;
            link = Activator.CreateInstance(type);

            if (link is not IPersistFile file || link is not NativeMethods.IShellLinkW shellLink)
                return (null, null);

            file.Load(path, 0 /* STGM_READ */);

            var target = new System.Text.StringBuilder(1024);
            var arguments = new System.Text.StringBuilder(2048);

            shellLink.GetPath(target, target.Capacity, IntPtr.Zero, NativeMethods.SLGP_RAWPATH);
            shellLink.GetArguments(arguments, arguments.Capacity);

            return (
                target.Length > 0 ? Environment.ExpandEnvironmentVariables(target.ToString()) : null,
                arguments.Length > 0 ? arguments.ToString() : null);
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot read the shortcut '{path}': {ex.Message}");
            return (null, null);
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
                Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>The URL= line of an Internet shortcut (an INI file).</summary>
    private static string? ReadInternetShortcut(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path).Take(64))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    return trimmed[4..].Trim();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot read the internet shortcut '{path}': {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// The "mark of the web": browsers save a Zone.Identifier stream beside a
    /// downloaded file. Zone 3 is the Internet, 4 untrusted sites.
    /// </summary>
    private static bool IsFromTheInternet(string path)
    {
        try
        {
            // Read it directly: most files have no such stream, which lands in
            // the catch below rather than costing a separate existence check.
            foreach (var line in File.ReadLines(path + ":Zone.Identifier").Take(32))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(trimmed[7..], out var zone))
                {
                    return zone >= 3;
                }
            }
        }
        catch (Exception)
        {
            // No stream (not NTFS, or never downloaded): not from the internet.
        }

        return false;
    }
}
