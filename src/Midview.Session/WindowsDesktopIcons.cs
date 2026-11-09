using System.Runtime.InteropServices;

namespace Midview.Session;

// The student's Desktop icons through Explorer's own view of the Desktop: the
// shell window list finds the Desktop's window, its browser gives the active
// view, and the view's IFolderView2 reads and sets where each icon is (the
// documented way to move Desktop icons from another process). Run on an STA
// thread; every call reaches into Explorer, so each one looks the view up
// again and lets it go, and a missing Desktop (Explorer restarting) is null.
public sealed class WindowsDesktopIcons : IDesktopIcons
{
    private const int CsidlDesktop = 0;
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;
    private const uint SvgioAllView = 2;
    private const uint SvsiPositionItem = 0x80;
    private const uint FwfAutoArrange = 0x1;
    private const uint SigdnDesktopAbsoluteParsing = 0x80028000;

    private static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid TopLevelBrowserService = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    public IReadOnlyDictionary<string, IconPoint>? Read() => WithView(view =>
    {
        var icons = new Dictionary<string, IconPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, pidl) in Items(view))
        {
            try
            {
                view.GetItemPosition(pidl, out var point);
                icons[name] = new IconPoint(point.X, point.Y);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }
        return icons;
    });

    public bool TurnOffAutoArrange() => WithView(view =>
    {
        if (view.GetAutoArrange() != 0) // S_OK means it is on
        {
            return false;
        }
        view.SetCurrentFolderFlags(FwfAutoArrange, 0);
        return true;
    });

    public void Place(IReadOnlyDictionary<string, IconPoint> positions) => WithView(view =>
    {
        var pidls = new List<IntPtr>();
        var points = new List<Point>();
        try
        {
            foreach (var (name, pidl) in Items(view))
            {
                if (positions.TryGetValue(name, out var where))
                {
                    pidls.Add(pidl);
                    points.Add(new Point { X = where.X, Y = where.Y });
                }
                else
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
            if (pidls.Count > 0)
            {
                view.SelectAndPositionItems((uint)pidls.Count, pidls.ToArray(), points.ToArray(), SvsiPositionItem);
            }
        }
        finally
        {
            pidls.ForEach(Marshal.FreeCoTaskMem);
        }
        return true;
    });

    // Each icon's parsing name and its ID list, which the caller frees. A
    // Desktop icon's ID list is one level under the namespace's root, the
    // Desktop, so it is also an absolute one the shell can name.
    private static IEnumerable<(string Name, IntPtr Pidl)> Items(IFolderView2 view)
    {
        view.ItemCount(SvgioAllView, out var count);
        for (var i = 0; i < count; i++)
        {
            view.Item(i, out var pidl);
            if (pidl == IntPtr.Zero)
            {
                continue;
            }
            if (SHGetNameFromIDList(pidl, SigdnDesktopAbsoluteParsing, out var name) != 0 || string.IsNullOrEmpty(name))
            {
                Marshal.FreeCoTaskMem(pidl);
                continue;
            }
            yield return (name, pidl);
        }
    }

    // The Desktop's view handed to use, then let go; default when the Desktop
    // isn't there (Explorer not running, or still starting).
    private static T? WithView<T>(Func<IFolderView2, T> use)
    {
        object? shellWindows = null;
        object? dispatch = null;
        object? browser = null;
        object? view = null;
        try
        {
            shellWindows = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellWindowsClsid, throwOnError: true)!);
            object location = CsidlDesktop;
            object? root = null;
            dispatch = ((IShellWindows)shellWindows!).FindWindowSW(ref location, ref root, SwcDesktop, out _, SwfoNeedDispatch);
            if (dispatch is null)
            {
                return default;
            }

            var service = TopLevelBrowserService;
            var browserId = typeof(IShellBrowser).GUID;
            if (((IComServiceProvider)dispatch).QueryService(ref service, ref browserId, out browser) != 0 || browser is null)
            {
                return default;
            }
            ((IShellBrowser)browser).QueryActiveShellView(out view);
            return view is IFolderView2 folderView ? use(folderView) : default;
        }
        finally
        {
            foreach (var com in new[] { view, browser, dispatch, shellWindows })
            {
                if (com is not null && Marshal.IsComObject(com))
                {
                    Marshal.FinalReleaseComObject(com);
                }
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string name);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        // Only FindWindowSW is called; the ones before it hold their places.
        void GetCount();
        void Item();
        void NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? FindWindowSW(
            [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLoc,
            [In, MarshalAs(UnmanagedType.Struct)] ref object? pvarLocRoot,
            int swClass, out int phwnd, int swfwOptions);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppvObject);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // Only QueryActiveShellView is called; the ones before it hold their places.
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();
        void QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object ppshv);
    }

    [ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView2
    {
        // IFolderView
        void GetCurrentViewMode(out uint pViewMode);
        void SetCurrentViewMode(uint viewMode);
        void GetFolder();
        void Item(int iItemIndex, out IntPtr ppidl);
        void ItemCount(uint uFlags, out int pcItems);
        void Items();
        void GetSelectionMarkedItem(out int piItem);
        void GetFocusedItem(out int piItem);
        void GetItemPosition(IntPtr pidl, out Point ppt);
        void GetSpacing(IntPtr ppt);
        void GetDefaultSpacing(out Point ppt);
        [PreserveSig]
        int GetAutoArrange();
        void SelectItem(int iItem, uint dwFlags);
        void SelectAndPositionItems(
            uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            [MarshalAs(UnmanagedType.LPArray)] Point[] apt, uint dwFlags);

        // IFolderView2, up to the folder flags
        void SetGroupBy();
        void GetGroupBy();
        void SetViewProperty();
        void GetViewProperty();
        void SetTileViewProperties();
        void SetExtendedTileViewProperties();
        void SetText();
        void SetCurrentFolderFlags(uint dwMask, uint dwFlags);
        void GetCurrentFolderFlags(out uint pdwFlags);
    }
}
