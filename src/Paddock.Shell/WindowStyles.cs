namespace Paddock.Shell;

/// <summary>Small window-style adjustments the WPF layer cannot make itself.</summary>
public static class WindowStyles
{
    /// <summary>
    /// Turns a window into a pure visual: the mouse goes straight through it,
    /// it is never activated, and it has no Alt-Tab entry. Used for the
    /// rectangle shown while a paddock is drawn on the desktop.
    /// </summary>
    public static void MakeClickThroughOverlay(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return;

        var exStyle = (long)NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(
            windowHandle,
            NativeMethods.GWL_EXSTYLE,
            (IntPtr)(exStyle
                     | NativeMethods.WS_EX_TRANSPARENT
                     | NativeMethods.WS_EX_NOACTIVATE
                     | NativeMethods.WS_EX_TOOLWINDOW));
    }
}
