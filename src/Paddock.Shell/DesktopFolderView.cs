using System.Runtime.InteropServices;
using Paddock.Core.Services;

namespace Paddock.Shell;

/// <summary>
/// Reads where Explorer has drawn each desktop icon, and which file it is,
/// through the desktop's own folder view — the documented route
/// (IShellWindows → the desktop's IShellBrowser → its view's IFolderView), and
/// the only one that gives real file paths: the icons' labels hide extensions,
/// so "Report" could be Report.docx or Report.pdf.
///
/// These are cross-process COM calls into Explorer. Call from a background
/// thread, never the UI thread.
/// </summary>
internal static class DesktopFolderView
{
    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    private const int CsidlDesktop = 0;
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;
    private const uint SvgioAllView = 2;
    private const uint SigdnFileSysPath = 0x80058000;

    /// <summary>
    /// Every desktop item that is a file or folder, with the centre of its icon
    /// in physical screen pixels. Empty when Explorer cannot be asked.
    /// </summary>
    internal static IReadOnlyList<DesktopIconSpot> GetIconSpots()
    {
        var previousDpi = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        object? windows = null;
        object? desktop = null;
        object? browser = null;
        object? view = null;

        try
        {
            var listView = DesktopListView.Find();
            if (listView == IntPtr.Zero)
                return [];

            var type = Type.GetTypeFromCLSID(ClsidShellWindows, throwOnError: true)!;
            windows = Activator.CreateInstance(type);
            if (windows is not IShellWindows shellWindows)
                return [];

            object location = CsidlDesktop;
            object? root = null;
            desktop = shellWindows.FindWindowSW(ref location, ref root, SwcDesktop, out _, SwfoNeedDispatch);
            if (desktop is not IOleServiceProvider provider)
                return [];

            var service = SidTopLevelBrowser;
            var iid = IidShellBrowser;
            if (provider.QueryService(ref service, ref iid, out var browserPointer) != 0 || browserPointer == IntPtr.Zero)
                return [];

            try
            {
                browser = Marshal.GetObjectForIUnknown(browserPointer);
            }
            finally
            {
                Marshal.Release(browserPointer);
            }

            if (browser is not IShellBrowser shellBrowser
                || shellBrowser.QueryActiveShellView(out view) != 0
                || view is not IFolderView folderView)
            {
                return [];
            }

            return ReadSpots(folderView, listView);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read where the desktop icons are: {ex.Message}");
            return [];
        }
        finally
        {
            foreach (var comObject in new[] { view, browser, desktop, windows })
            {
                if (comObject is not null && Marshal.IsComObject(comObject))
                    Marshal.ReleaseComObject(comObject);
            }

            NativeMethods.SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    private static List<DesktopIconSpot> ReadSpots(IFolderView view, IntPtr listView)
    {
        var spots = new List<DesktopIconSpot>();

        if (view.ItemCount(SvgioAllView, out var count) != 0)
            return spots;

        // Positions are an item's top-left; its cell is one icon spacing across.
        var spacing = new NativeMethods.POINT();
        view.GetSpacing(ref spacing);

        for (var i = 0; i < count; i++)
        {
            if (view.Item(i, out var pidl) != 0 || pidl == IntPtr.Zero)
                continue;

            try
            {
                if (view.GetItemPosition(pidl, out var position) != 0)
                    continue;

                // The desktop is the root of the shell namespace, so its items'
                // IDs are absolute. Recycle Bin, This PC and friends have no
                // file-system path and are skipped here.
                if (NativeMethods.SHGetNameFromIDList(pidl, SigdnFileSysPath, out var name) != 0 || name == IntPtr.Zero)
                    continue;

                string? path;
                try
                {
                    path = Marshal.PtrToStringUni(name);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(name);
                }

                if (string.IsNullOrEmpty(path))
                    continue;

                var centre = new NativeMethods.POINT
                {
                    X = position.X + spacing.X / 2,
                    Y = position.Y + spacing.Y / 2,
                };
                NativeMethods.ClientToScreen(listView, ref centre);

                spots.Add(new DesktopIconSpot(path, centre.X, centre.Y));
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        return spots;
    }

    // ── COM interfaces (only the slots up to the methods used matter) ───────

    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object Item([MarshalAs(UnmanagedType.Struct)] object index);

        [return: MarshalAs(UnmanagedType.IUnknown)]
        object NewEnum();

        void Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int cookie);

        void RegisterPending(
            int threadId,
            [MarshalAs(UnmanagedType.Struct)] ref object location,
            [MarshalAs(UnmanagedType.Struct)] ref object locationRoot,
            int swClass,
            out int cookie);

        void Revoke(int cookie);

        void OnNavigate(int cookie, [MarshalAs(UnmanagedType.Struct)] ref object location);

        void OnActivated(int cookie, [MarshalAs(UnmanagedType.VariantBool)] bool active);

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? FindWindowSW(
            [MarshalAs(UnmanagedType.Struct)] ref object location,
            [MarshalAs(UnmanagedType.Struct)] ref object? locationRoot,
            int swClass,
            out int hwnd,
            int options);

        void OnCreated(int cookie, [MarshalAs(UnmanagedType.IUnknown)] object punk);

        void ProcessAttachDetach([MarshalAs(UnmanagedType.VariantBool)] bool attach);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, out IntPtr ppv);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out IntPtr hwnd);
        [PreserveSig] int ContextSensitiveHelp(int enterMode);
        [PreserveSig] int InsertMenusSB(IntPtr sharedMenu, IntPtr menuWidths);
        [PreserveSig] int SetMenuSB(IntPtr sharedMenu, IntPtr oleMenu, IntPtr activeObject);
        [PreserveSig] int RemoveMenusSB(IntPtr sharedMenu);
        [PreserveSig] int SetStatusTextSB(IntPtr statusText);
        [PreserveSig] int EnableModelessSB(int enable);
        [PreserveSig] int TranslateAcceleratorSB(IntPtr message, ushort id);
        [PreserveSig] int BrowseObject(IntPtr pidl, uint flags);
        [PreserveSig] int GetViewStateStream(uint mode, out IntPtr stream);
        [PreserveSig] int GetControlWindow(uint id, out IntPtr hwnd);
        [PreserveSig] int SendControlMsg(uint id, uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object? shellView);
        [PreserveSig] int OnViewWindowActive(IntPtr shellView);
        [PreserveSig] int SetToolbarItems(IntPtr buttons, uint count, uint flags);
    }

    [ComImport]
    [Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out uint viewMode);
        [PreserveSig] int SetCurrentViewMode(uint viewMode);
        [PreserveSig] int GetFolder(ref Guid riid, out IntPtr folder);
        [PreserveSig] int Item(int index, out IntPtr pidl);
        [PreserveSig] int ItemCount(uint flags, out int count);
        [PreserveSig] int Items(uint flags, ref Guid riid, out IntPtr items);
        [PreserveSig] int GetSelectionMarkedItem(out int item);
        [PreserveSig] int GetFocusedItem(out int item);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out NativeMethods.POINT point);
        [PreserveSig] int GetSpacing(ref NativeMethods.POINT point);
        [PreserveSig] int GetDefaultSpacing(out NativeMethods.POINT point);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int item, uint flags);
        [PreserveSig] int SelectAndPositionItems(uint count, IntPtr pidls, IntPtr points, uint flags);
    }
}
