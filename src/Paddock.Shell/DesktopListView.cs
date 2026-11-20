using System.Runtime.InteropServices;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Questions about Explorer's desktop icon view (a SysListView32 inside
/// SHELLDLL_DefView) asked from outside Explorer's process.
/// </summary>
internal static class DesktopListView
{
    /// <summary>
    /// Longest Paddock will wait on Explorer for an answer. This is asked from a
    /// low-level mouse hook, which must never keep the mouse waiting long.
    /// </summary>
    private const uint TimeoutMs = 150;

    /// <summary>
    /// True when <paramref name="screenPoint"/> (physical pixels) is on the
    /// desktop's icon view but on no icon — empty desktop. False when it is on
    /// an icon, on any other window, or when Explorer could not be asked.
    /// </summary>
    internal static bool IsBareDesktop(NativeMethods.POINT screenPoint)
    {
        var hit = NativeMethods.WindowFromPoint(screenPoint);
        if (hit == IntPtr.Zero || NativeMethods.GetWindowClassName(hit) != "SysListView32")
            return false;

        if (NativeMethods.GetWindowClassName(NativeMethods.GetParent(hit)) != "SHELLDLL_DefView")
            return false;

        // The desktop's view, not a list view in some Explorer folder window.
        var root = NativeMethods.GetWindowClassName(NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT));
        if (root is not ("Progman" or "WorkerW"))
            return false;

        return HitTest(hit, screenPoint) == -1;
    }

    /// <summary>
    /// The index of the icon under a screen point, -1 for none, or null when
    /// Explorer could not be asked. LVM_HITTEST needs its argument in the list
    /// view's own process, so the struct is written into Explorer's memory.
    /// </summary>
    private static int? HitTest(IntPtr listView, NativeMethods.POINT screenPoint)
    {
        NativeMethods.GetWindowThreadProcessId(listView, out var processId);

        var process = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE,
            false,
            processId);

        if (process == IntPtr.Zero)
            return null;

        var size = (nuint)Marshal.SizeOf<NativeMethods.LVHITTESTINFO>();
        var remote = IntPtr.Zero;

        try
        {
            remote = NativeMethods.VirtualAllocEx(
                process, IntPtr.Zero, size, NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE, NativeMethods.PAGE_READWRITE);

            if (remote == IntPtr.Zero)
                return null;

            var client = screenPoint;
            NativeMethods.ScreenToClient(listView, ref client);

            var info = new NativeMethods.LVHITTESTINFO { pt = client, iItem = -1 };
            if (!NativeMethods.WriteProcessMemory(process, remote, ref info, size, out _))
                return null;

            if (NativeMethods.SendMessageTimeoutW(
                    listView,
                    NativeMethods.LVM_HITTEST,
                    IntPtr.Zero,
                    remote,
                    NativeMethods.SMTO_ABORTIFHUNG,
                    TimeoutMs,
                    out var result) == IntPtr.Zero)
            {
                return null;
            }

            return (int)result.ToInt64();
        }
        catch (Exception ex)
        {
            Log.Warn($"Desktop icon hit test failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (remote != IntPtr.Zero)
                NativeMethods.VirtualFreeEx(process, remote, 0, NativeMethods.MEM_RELEASE);

            NativeMethods.CloseHandle(process);
        }
    }
}
