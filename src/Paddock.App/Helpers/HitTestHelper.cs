using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Paddock.App.Helpers;

/// <summary>
/// Provides Win32-level click-through (WS_EX_TRANSPARENT | WS_EX_LAYERED)
/// helpers for overlay windows. In practice, the WPF AllowsTransparency +
/// transparent Canvas background already provides click-through for empty
/// areas; these methods exist for scenarios where explicit Win32 style
/// manipulation is needed.
/// </summary>
internal static class HitTestHelper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    /// <summary>
    /// Sets WS_EX_TRANSPARENT | WS_EX_LAYERED on the window, making it
    /// completely invisible to hit-testing at the Win32 level. All mouse
    /// events will pass through to windows below.
    /// WARNING: This makes the entire window (including child controls)
    /// unclickable. Use only when the overlay should be fully passive.
    /// </summary>
    internal static void SetClickThrough(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE,
            (IntPtr)((long)style | WS_EX_TRANSPARENT | WS_EX_LAYERED));
    }

    /// <summary>
    /// Removes WS_EX_TRANSPARENT from the window, restoring normal
    /// hit-testing. WS_EX_LAYERED is left intact because WPF needs it
    /// when AllowsTransparency is true.
    /// </summary>
    internal static void RemoveClickThrough(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE,
            (IntPtr)((long)style & ~WS_EX_TRANSPARENT));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
