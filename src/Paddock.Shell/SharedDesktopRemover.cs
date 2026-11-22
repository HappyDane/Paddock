using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Removes originals from the all-users desktop after Paddock has copied them
/// into a paddock.
///
/// Normal accounts cannot delete there, so when a direct delete is refused
/// Paddock starts a second copy of itself elevated, once for the whole batch,
/// with <see cref="HelperSwitch"/>. Windows shows its usual permission prompt —
/// the same one Explorer shows when you move one of these shortcuts by hand.
/// Declining it simply leaves the originals in place.
/// </summary>
public sealed class SharedDesktopRemover : ISharedDesktopRemover
{
    /// <summary>Command-line switch that runs Paddock as the elevated helper.</summary>
    public const string HelperSwitch = "--remove-shared-desktop-items";

    private const int ErrorCancelled = 1223;
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromMinutes(2);

    private readonly string _sharedDesktopRoot;

    public SharedDesktopRemover(string sharedDesktopRoot)
    {
        _sharedDesktopRoot = sharedDesktopRoot;
    }

    public IReadOnlyCollection<string> Remove(IReadOnlyList<string> paths)
    {
        var removed = new List<string>();
        var refused = new List<string>();

        foreach (var path in paths)
        {
            if (!IsDirectlyIn(path, _sharedDesktopRoot))
                continue;

            try
            {
                File.Delete(path);
                removed.Add(path);
            }
            catch (UnauthorizedAccessException)
            {
                refused.Add(path);
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not remove '{path}' from the all-users desktop: {ex.Message}");
            }
        }

        if (refused.Count > 0)
        {
            RunElevatedHelper(refused);
            removed.AddRange(refused.Where(p => !File.Exists(p)));
        }

        return removed;
    }

    /// <summary>
    /// Entry point for the elevated copy: deletes the files listed in
    /// <paramref name="listFile"/>, one path per line, but only ones that sit
    /// directly on the all-users desktop. Returns the process exit code.
    /// </summary>
    public static int RunHelper(string listFile)
    {
        var sharedRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        var failures = 0;

        try
        {
            foreach (var line in File.ReadAllLines(listFile))
            {
                var path = line.Trim();
                if (path.Length == 0)
                    continue;

                // This process runs with administrator rights: never act on a
                // path the list should not contain, whatever wrote it.
                if (!IsDirectlyIn(path, sharedRoot))
                {
                    Log.Warn($"Elevated helper refused '{path}': not on the all-users desktop.");
                    failures++;
                    continue;
                }

                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Elevated helper could not remove '{path}': {ex.Message}");
                    failures++;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Elevated helper could not read '{listFile}'.", ex);
            return 2;
        }

        return failures == 0 ? 0 : 1;
    }

    private static void RunElevatedHelper(IReadOnlyList<string> paths)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return;

        var listFile = Path.Combine(Path.GetTempPath(), $"Paddock-shared-{Guid.NewGuid():N}.txt");

        // Starting the helper waits for the user to answer the permission
        // prompt, however long that takes: not a freeze.
        using var waiting = AppHeartbeat.Current?.WaitingOnPurpose();

        try
        {
            File.WriteAllLines(listFile, paths);

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"{HelperSwitch} \"{listFile}\"",
                UseShellExecute = true,
                Verb = "runas",
            });

            if (process is null)
                return;

            if (!process.WaitForExit(HelperTimeout))
                Log.Warn("The elevated helper did not finish in time.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            Log.Info("Permission to change the all-users desktop was declined.");
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the elevated helper.", ex);
        }
        finally
        {
            try
            {
                File.Delete(listFile);
            }
            catch
            {
                // A stray temp file is harmless.
            }
        }
    }

    private static bool IsDirectlyIn(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder))
            return false;

        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(path));
            return parent is not null
                   && string.Equals(
                       parent.TrimEnd(Path.DirectorySeparatorChar),
                       Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
