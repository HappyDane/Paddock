using System.Runtime.InteropServices;

namespace Paddock.Shell;

/// <summary>
/// Handles embedding the overlay window into the Windows desktop shell.
/// Uses the Progman/WorkerW technique to render behind desktop icons.
/// </summary>
public class ShellHookService
{
    private IntPtr _workerW;

    /// <summary>
    /// Finds or creates the WorkerW window behind desktop icons.
    /// This is the target parent for our overlay window.
    /// </summary>
    public IntPtr GetDesktopWorkerW()
    {
        // Step 1: Find the Progman window
        var progman = NativeMethods.FindWindowW("Progman", null);
        if (progman == IntPtr.Zero)
            return IntPtr.Zero;

        // Step 2: Send the undocumented message to spawn a WorkerW behind icons
        NativeMethods.SendMessageTimeoutW(
            progman,
            NativeMethods.WM_SPAWN_WORKER,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.SMTO_NORMAL,
            1000,
            out _);

        // Step 3: Find the WorkerW window that sits behind the desktop icons
        _workerW = IntPtr.Zero;
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            var shellDef = NativeMethods.FindWindowExW(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellDef != IntPtr.Zero)
            {
                // The WorkerW we want is the NEXT sibling after the one containing SHELLDLL_DefView
                _workerW = NativeMethods.FindWindowExW(IntPtr.Zero, hWnd, "WorkerW", null);
            }
            return true;
        }, IntPtr.Zero);

        return _workerW;
    }

    /// <summary>
    /// Embeds a WPF window as a child of the desktop WorkerW.
    /// </summary>
    public bool EmbedInDesktop(IntPtr wpfWindowHandle)
    {
        var workerW = GetDesktopWorkerW();
        if (workerW == IntPtr.Zero)
            return false;

        NativeMethods.SetParent(wpfWindowHandle, workerW);
        return true;
    }
}
