namespace Paddock.Shell;

/// <summary>
/// Configures the overlay window so it lives on the Windows desktop:
/// hidden from Alt-Tab and the taskbar, and initially pinned to the
/// bottom of the Z-order so real application windows naturally render
/// on top of it — just like Stardock Fences.
/// </summary>
public class ShellHookService
{
    /// <summary>
    /// Applies the "desktop layer" window styles to the given window handle
    /// and pushes it to the bottom of the Z-order. Call this once, right
    /// after the window has been created.
    /// </summary>
    public void PinToDesktopLayer(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        // Adjust extended window styles:
        //   + WS_EX_TOOLWINDOW  → hide from Alt-Tab and the taskbar
        //   − WS_EX_APPWINDOW   → make sure WPF didn't add it
        long exStyle = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
        exStyle &= ~(long)NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)exStyle);

        // Push the window to the bottom of the non-topmost Z-order without
        // moving, resizing, or activating it. After this, Windows handles
        // Z-order naturally: clicking the overlay brings paddocks forward
        // for interaction (rename, drag, etc.), clicking any other window
        // sends them back behind.
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_BOTTOM,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
