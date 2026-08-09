using System.Runtime.InteropServices;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Shows the native Windows Explorer context menu for a file or folder,
/// identical to the menu shown when right-clicking an item on the desktop.
/// </summary>
public static class ShellContextMenu
{
    // ── COM GUIDs ──────────────────────────────────────────────────────

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    // ── COM interfaces ─────────────────────────────────────────────────

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        void ParseDisplayName(
            IntPtr hwnd,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            out uint pchEaten,
            out IntPtr ppidl,
            ref uint pdwAttributes);

        void EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);

        void BindToObject(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);

        void BindToStorage(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        void CreateViewObject(IntPtr hwndOwner, [In] ref Guid riid, out IntPtr ppv);

        void GetAttributesOf(uint cidl,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            ref uint rgfInOut);

        void GetUIObjectOf(
            IntPtr hwndOwner,
            uint cidl,
            [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            [In] ref Guid riid,
            IntPtr rgfReserved,
            out IntPtr ppv);

        void GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);

        void SetNameOf(IntPtr hwnd, IntPtr pidl,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst,
            uint idCmdLast, uint uFlags);

        void InvokeCommand(ref CMINVOKECOMMANDINFO pici);

        void GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved,
            IntPtr pszName, uint cchMax);
    }

    // ── Structs ────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;       // MAKEINTRESOURCE(cmd)
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    // ── P/Invoke ───────────────────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetDesktopFolder(out IntPtr ppshf);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(
        IntPtr pidl,
        [In] ref Guid riid,
        out IntPtr ppv,
        out IntPtr ppidlLast);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr lptpm);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint CMF_NORMAL = 0x00000000;
    private const uint CMF_EXPLORE = 0x00000004;
    private const int SW_SHOWNORMAL = 1;
    private const uint COINIT_APARTMENTTHREADED = 0x2;

    // ── Public API ─────────────────────────────────────────────────────

    /// <summary>
    /// Shows the native Windows shell context menu for the given file path
    /// at the specified screen coordinates.
    /// </summary>
    /// <param name="filePath">Absolute path to the file or folder.</param>
    /// <param name="windowHandle">HWND of the owner window.</param>
    /// <param name="x">Screen X coordinate for the menu.</param>
    /// <param name="y">Screen Y coordinate for the menu.</param>
    public static void Show(string filePath, IntPtr windowHandle, int x, int y)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        IntPtr pidlFull = IntPtr.Zero;
        IntPtr hMenu = IntPtr.Zero;
        IShellFolder? folder = null;
        IContextMenu? contextMenu = null;
        var comInitialized = false;

        try
        {
            // Ensure COM is initialised on this thread.
            int coHr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
            comInitialized = coHr == 0 || coHr == 1; // S_OK or S_FALSE

            // Parse the full path into an absolute PIDL.
            uint sfgao = 0;
            int hr = SHParseDisplayName(filePath, IntPtr.Zero, out pidlFull, 0, out sfgao);
            if (hr != 0 || pidlFull == IntPtr.Zero)
                return;

            // Bind to the parent folder and get the child PIDL.
            Guid iidShellFolder = IID_IShellFolder;
            hr = SHBindToParent(pidlFull, ref iidShellFolder, out IntPtr ppvFolder, out IntPtr pidlChild);
            if (hr != 0 || ppvFolder == IntPtr.Zero)
                return;

            folder = (IShellFolder)Marshal.GetObjectForIUnknown(ppvFolder);
            Marshal.Release(ppvFolder);

            // Ask for IContextMenu on the child item.
            Guid iidContextMenu = IID_IContextMenu;
            folder.GetUIObjectOf(
                windowHandle,
                1,
                new[] { pidlChild },
                ref iidContextMenu,
                IntPtr.Zero,
                out IntPtr ppvContextMenu);

            if (ppvContextMenu == IntPtr.Zero)
                return;

            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(ppvContextMenu);
            Marshal.Release(ppvContextMenu);

            // Build the popup menu.
            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero)
                return;

            hr = contextMenu.QueryContextMenu(hMenu, 0, 1, 0x7FFF, CMF_NORMAL | CMF_EXPLORE);
            if (hr < 0)
                return;

            // Show the menu and wait for the user's choice.
            uint cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_LEFTALIGN, x, y, windowHandle, IntPtr.Zero);

            if (cmd >= 1)
            {
                var ci = new CMINVOKECOMMANDINFO
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                    fMask = 0,
                    hwnd = windowHandle,
                    lpVerb = (IntPtr)(cmd - 1),   // zero-based offset
                    lpParameters = IntPtr.Zero,
                    lpDirectory = IntPtr.Zero,
                    nShow = SW_SHOWNORMAL,
                    dwHotKey = 0,
                    hIcon = IntPtr.Zero
                };

                contextMenu.InvokeCommand(ref ci);
            }
        }
        catch (Exception ex)
        {
            // Swallow errors — if COM fails we simply don't show a menu.
            Log.Error($"Could not show the shell context menu for '{filePath}'.", ex);
        }
        finally
        {
            if (hMenu != IntPtr.Zero)
                DestroyMenu(hMenu);

            if (contextMenu is not null)
                Marshal.ReleaseComObject(contextMenu);

            if (folder is not null)
                Marshal.ReleaseComObject(folder);

            if (pidlFull != IntPtr.Zero)
                CoTaskMemFree(pidlFull);

            if (comInitialized)
                CoUninitialize();
        }
    }
}
