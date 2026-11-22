using System.Runtime.InteropServices;
using Paddock.Core.Services;

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

    /// <summary>
    /// Frosted glass: asks the desktop compositor to draw a blurred copy of
    /// whatever is behind the window (the wallpaper, for a paddock) underneath
    /// it. The window's own see-through pixels then show the blur, so its
    /// background colour becomes a tint over frosted glass.
    ///
    /// The blur fills the window's whole rectangle; pair it with
    /// <see cref="SetRoundedRegion"/> so it follows rounded corners. Returns
    /// false where Windows does not offer it (the paddock then stays plain).
    /// </summary>
    public static bool SetBlurBehind(IntPtr windowHandle, bool enabled)
    {
        if (windowHandle == IntPtr.Zero)
            return false;

        var accent = new NativeMethods.AccentPolicy
        {
            AccentState = enabled ? NativeMethods.ACCENT_ENABLE_BLURBEHIND : NativeMethods.ACCENT_DISABLED,
        };

        var size = Marshal.SizeOf<NativeMethods.AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(accent, buffer, fDeleteOld: false);

            var data = new NativeMethods.WindowCompositionAttributeData
            {
                Attribute = NativeMethods.WCA_ACCENT_POLICY,
                Data = buffer,
                SizeOfData = size,
            };

            if (NativeMethods.SetWindowCompositionAttribute(windowHandle, ref data))
                return true;

            Log.Warn($"Windows declined the frosted-glass effect (error {Marshal.GetLastWin32Error()}).");
            return false;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            Log.Warn("This version of Windows has no frosted-glass effect; paddocks stay plain.");
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Clips the window to a rounded rectangle (physical pixels). Everything
    /// outside — including the compositor's blur — is cut away and clicks
    /// there fall through to the desktop. Pass a radius of zero or less to
    /// remove the clip again.
    /// </summary>
    public static void SetRoundedRegion(IntPtr windowHandle, int width, int height, int radius)
    {
        if (windowHandle == IntPtr.Zero)
            return;

        if (radius <= 0 || width <= 0 || height <= 0)
        {
            NativeMethods.SetWindowRgn(windowHandle, IntPtr.Zero, true);
            return;
        }

        // The ellipse size is the corner's diameter; the +1s cover the
        // exclusive right and bottom edges of a GDI region.
        var region = NativeMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == IntPtr.Zero)
            return;

        if (NativeMethods.SetWindowRgn(windowHandle, region, true) == 0)
            NativeMethods.DeleteObject(region);
    }
}
