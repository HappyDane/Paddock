using System.Runtime.InteropServices;

namespace Paddock.Shell;

/// <summary>
/// P/Invoke declarations for Win32 APIs used by Paddock.
/// </summary>
internal static partial class NativeMethods
{
    // -- Window Management --

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr FindWindowW(
        [MarshalAs(UnmanagedType.LPWStr)] string? lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpWindowName);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr FindWindowExW(
        IntPtr hwndParent,
        IntPtr hwndChildAfter,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpszClass,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpszWindow);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr SendMessageTimeoutW(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetClassNameW(
        IntPtr hWnd,
        [MarshalAs(UnmanagedType.LPWStr)] char[] lpClassName,
        int nMaxCount);

    // -- Desktop ListView (icon positions) --

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetShellWindow();

    internal const uint LVM_GETITEMCOUNT = 0x1004;
    internal const uint LVM_GETITEMPOSITION = 0x1010;
    internal const uint LVM_SETITEMPOSITION = 0x100F;

    // -- Message constants --

    internal const uint SMTO_NORMAL = 0x0000;
    internal const uint WM_SPAWN_WORKER = 0x052C;
}
