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

    /// <summary>The desktop's icon view window, or zero when there is none.</summary>
    internal static IntPtr Find()
    {
        var hosts = new List<IntPtr> { NativeMethods.GetShellWindow() };

        var worker = IntPtr.Zero;
        while ((worker = NativeMethods.FindWindowExW(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            hosts.Add(worker);

        foreach (var host in hosts)
        {
            var view = NativeMethods.FindWindowExW(host, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero)
                return NativeMethods.FindWindowExW(view, IntPtr.Zero, "SysListView32", null);
        }

        return IntPtr.Zero;
    }

    /// <summary>Where a point lands, as far as the desktop is concerned.</summary>
    internal enum Surface
    {
        /// <summary>Some window that is not the desktop.</summary>
        NotDesktop,

        /// <summary>The desktop's icon view (it may or may not be on an icon).</summary>
        IconView,

        /// <summary>The desktop itself with its icons hidden: nothing there but wallpaper.</summary>
        Wallpaper,
    }

    /// <summary>
    /// Says whether a point (physical pixels) is on the desktop, from window
    /// classes alone — no message is sent, so this is cheap enough for every
    /// click a low-level hook sees.
    /// </summary>
    internal static Surface SurfaceAt(NativeMethods.POINT screenPoint)
    {
        var hit = NativeMethods.WindowFromPoint(screenPoint);
        if (hit == IntPtr.Zero)
            return Surface.NotDesktop;

        var root = NativeMethods.GetWindowClassName(NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT));
        if (root is not ("Progman" or "WorkerW"))
            return Surface.NotDesktop;

        return NativeMethods.GetWindowClassName(hit) switch
        {
            "SysListView32" when NativeMethods.GetWindowClassName(NativeMethods.GetParent(hit)) == "SHELLDLL_DefView"
                => Surface.IconView,
            "SHELLDLL_DefView" or "Progman" or "WorkerW" => Surface.Wallpaper,
            _ => Surface.NotDesktop,
        };
    }

    /// <summary>
    /// True when a double-click at this point was on empty desktop: the icon
    /// view with no icon there, or the desktop with its icons hidden.
    /// </summary>
    internal static bool IsEmptyDesktop(NativeMethods.POINT screenPoint) => SurfaceAt(screenPoint) switch
    {
        Surface.Wallpaper => true,
        Surface.IconView => HitTest(NativeMethods.WindowFromPoint(screenPoint), screenPoint) == -1,
        _ => false,
    };

    /// <summary>
    /// True while a desktop icon is being renamed (its label is an edit box).
    /// An item must not be moved away from under the user's typing.
    /// </summary>
    internal static bool IsRenamingAnIcon()
    {
        var listView = Find();
        if (listView == IntPtr.Zero)
            return false;

        return NativeMethods.SendMessageTimeoutW(
                   listView,
                   NativeMethods.LVM_GETEDITCONTROL,
                   IntPtr.Zero,
                   IntPtr.Zero,
                   NativeMethods.SMTO_ABORTIFHUNG,
                   TimeoutMs,
                   out var edit) != IntPtr.Zero
               && edit != IntPtr.Zero;
    }

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
