using System.Runtime.InteropServices;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

internal sealed class WindowInfo
{
    public required nint Hwnd { get; init; }
    public required string Title { get; init; }
    public required bool Minimized { get; init; }

    /// <summary>
    /// The visible frame in screen pixels; for a minimized window, the window rectangle it restores to
    /// (screen pixels, invisible resize border included).
    /// </summary>
    public required RECT Bounds { get; init; }

    public required uint ProcessId { get; init; }

    /// <summary>The whole window rectangle, invisible resize borders included; empty for a minimized window.</summary>
    public required RECT WindowRect { get; init; }

    /// <summary>The monitor the window counts as being on.</summary>
    public required nint Monitor { get; init; }

    /// <summary>Front-to-back position among all top-level windows when the list was made (0 = front).</summary>
    public required int ZOrder { get; init; }
}

/// <summary>One row of the popup: the windows of one program, in z-order.</summary>
internal sealed class ProgramGroup
{
    public required string Key { get; init; }
    public string? ExePath { get; init; }
    public List<WindowInfo> Windows { get; } = [];
}

/// <summary>
/// The windows Alt-Tab would list, on one monitor or on all of them, grouped by program. Groups come in
/// the z-order of their topmost window (the popup re-sorts them with <see cref="WindowOrder"/>).
/// </summary>
internal static unsafe class WindowList
{
    const string FrameHostExe = "ApplicationFrameHost.exe";
    const string CoreWindowClass = "Windows.UI.Core.CoreWindow";
    static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    static readonly Guid FMTID_AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    const ushort VT_LPWSTR = 31;
    const int MinWindowSize = 32;

    public static List<ProgramGroup> ForMonitor(nint monitor) => Collect(monitor);

    public static List<ProgramGroup> AllMonitors() => Collect(0);

    /// <param name="onlyMonitor">The monitor to list, or 0 for every monitor.</param>
    static List<ProgramGroup> Collect(nint onlyMonitor)
    {
        var hwnds = new List<nint>();
        var handle = GCHandle.Alloc(hwnds);
        try
        {
            User32.EnumWindows(&CollectWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        nint shell = User32.GetShellWindow();
        uint ownPid = (uint)Environment.ProcessId;
        var paths = new Dictionary<uint, string?>();
        var monitorAreas = new Dictionary<nint, (RECT, RECT)>();
        var groups = new List<ProgramGroup>();
        var byKey = new Dictionary<string, ProgramGroup>(StringComparer.OrdinalIgnoreCase);

        for (int z = 0; z < hwnds.Count; z++)
        {
            nint hwnd = hwnds[z];
            if (hwnd == shell || !IsSwitchable(hwnd)) continue;
            nint monitor = User32.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (onlyMonitor != 0 && monitor != onlyMonitor) continue;
            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid) continue;

            bool minimized = User32.IsIconic(hwnd);
            RECT bounds = minimized ? RestoredBounds(hwnd, MonitorAreas(monitor, monitorAreas)) : VisibleBounds(hwnd);
            // Windows this small are helpers, not something to switch to (seen: a 14x14 px WPF window
            // with a hidden owner, which passes the Alt-Tab test and gave an empty tile).
            if (bounds.Width < MinWindowSize || bounds.Height < MinWindowSize) continue;

            string title = User32.GetText(hwnd);
            if (title.Length == 0) continue;

            RECT windowRect = default;
            if (!minimized) User32.GetWindowRect(hwnd, out windowRect);

            var (key, exe) = ProgramOf(hwnd, pid, paths);
            if (!byKey.TryGetValue(key, out var group))
            {
                group = new ProgramGroup { Key = key, ExePath = exe };
                byKey.Add(key, group);
                groups.Add(group);
            }
            group.Windows.Add(new WindowInfo
            {
                Hwnd = hwnd,
                Title = title,
                Minimized = minimized,
                Bounds = bounds,
                WindowRect = windowRect,
                ProcessId = pid,
                Monitor = monitor,
                ZOrder = z,
            });
        }
        return groups;
    }

    [UnmanagedCallersOnly]
    static int CollectWindow(nint hwnd, nint param)
    {
        ((List<nint>)GCHandle.FromIntPtr(param).Target!).Add(hwnd);
        return 1;
    }

    static bool IsSwitchable(nint hwnd)
    {
        if (!User32.IsWindowVisible(hwnd)) return false;

        long ex = User32.GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        bool appWindow = (ex & WS_EX_APPWINDOW) != 0;
        if (!appWindow)
        {
            if ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_NOACTIVATE) != 0) return false;
            if (!IsAltTabRoot(hwnd)) return false;
        }

        int cloaked = 0;
        if (Dwm.DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) >= 0 && cloaked != 0)
            return false;

        return User32.GetWindowTextLengthW(hwnd) > 0;
    }

    /// <summary>
    /// The classic Alt-Tab test: walk from the root owner through hidden last-active popups. The window
    /// is listed if the walk ends on it, or if it is the visible popup of an owner that is itself hidden
    /// (apps that parent their main window to an invisible one).
    /// </summary>
    static bool IsAltTabRoot(nint hwnd)
    {
        nint walk = User32.GetAncestor(hwnd, 3 /* GA_ROOTOWNER */);
        nint tried;
        while ((tried = User32.GetLastActivePopup(walk)) != walk)
        {
            if (User32.IsWindowVisible(tried)) break;
            walk = tried;
        }
        if (walk == hwnd) return true;
        return tried == hwnd && !User32.IsWindowVisible(walk);
    }

    static RECT VisibleBounds(nint hwnd)
    {
        RECT r;
        if (Dwm.DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &r, sizeof(RECT)) >= 0)
            return r;
        User32.GetWindowRect(hwnd, out r);
        return r;
    }

    static (RECT Bounds, RECT Work) MonitorAreas(nint monitor, Dictionary<nint, (RECT, RECT)> cache)
    {
        if (!cache.TryGetValue(monitor, out var areas))
        {
            var (bounds, work, _) = Monitors.Describe(monitor);
            cache[monitor] = areas = (bounds, work);
        }
        return areas;
    }

    /// <summary>The window rectangle a minimized window restores to, in screen coordinates.</summary>
    static RECT RestoredBounds(nint hwnd, (RECT Bounds, RECT Work) monitor)
    {
        WINDOWPLACEMENT wp = new() { length = (uint)sizeof(WINDOWPLACEMENT) };
        if (!User32.GetWindowPlacement(hwnd, &wp)) return default;
        if ((wp.flags & WPF_RESTORETOMAXIMIZED) != 0) return monitor.Work;
        // rcNormalPosition is in workspace coordinates: offset by where the work area starts on its monitor.
        int dx = monitor.Work.Left - monitor.Bounds.Left, dy = monitor.Work.Top - monitor.Bounds.Top;
        RECT r = wp.rcNormalPosition;
        return new RECT(r.Left + dx, r.Top + dy, r.Right + dx, r.Bottom + dy);
    }

    static (string Key, string? Exe) ProgramOf(nint hwnd, uint pid, Dictionary<uint, string?> paths)
    {
        string? exe = PathOf(pid, paths);
        if (exe is null) return ($"pid:{pid}", null);
        if (!exe.EndsWith(FrameHostExe, StringComparison.OrdinalIgnoreCase)) return (exe, exe);

        // A Store app's frame belongs to ApplicationFrameHost; the app itself owns the CoreWindow child.
        uint appPid = CoreWindowProcess(hwnd, pid);
        if (appPid != 0 && PathOf(appPid, paths) is { } appExe) return (appExe, appExe);

        // A minimized Store app has no CoreWindow child; its AppUserModelID still names it.
        if (AppUserModelId(hwnd) is { } aumid) return ($"aumid:{aumid}", null);
        return ($"hwnd:{hwnd}", exe);
    }

    static string? PathOf(uint pid, Dictionary<uint, string?> paths)
    {
        if (paths.TryGetValue(pid, out var cached)) return cached;
        string? path = null;
        nint process = Kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process != 0)
        {
            char* buffer = stackalloc char[1024];
            uint size = 1024;
            if (Kernel32.QueryFullProcessImageNameW(process, 0, buffer, &size))
                path = new string(buffer, 0, (int)size);
            Kernel32.CloseHandle(process);
        }
        paths[pid] = path;
        return path;
    }

    struct CoreWindowSearch
    {
        public uint FramePid;
        public uint FoundPid;
    }

    static uint CoreWindowProcess(nint frame, uint framePid)
    {
        var search = new CoreWindowSearch { FramePid = framePid };
        User32.EnumChildWindows(frame, &FindCoreWindow, (nint)(&search));
        return search.FoundPid;
    }

    [UnmanagedCallersOnly]
    static int FindCoreWindow(nint hwnd, nint param)
    {
        var search = (CoreWindowSearch*)param;
        char* name = stackalloc char[64];
        int length = User32.GetClassNameW(hwnd, name, 64);
        if (!new ReadOnlySpan<char>(name, length).SequenceEqual(CoreWindowClass)) return 1;
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == search->FramePid) return 1;
        search->FoundPid = pid;
        return 0;
    }

    /// <summary>PKEY_AppUserModel_ID through IPropertyStore::GetValue (slot 5).</summary>
    static string? AppUserModelId(nint hwnd)
    {
        Guid iid = IID_IPropertyStore;
        nint store;
        if (Shell32.SHGetPropertyStoreForWindow(hwnd, &iid, &store) < 0 || store == 0) return null;
        try
        {
            var key = new PROPERTYKEY { fmtid = FMTID_AppUserModel, pid = 5 };
            PROPVARIANT value = default;
            int hr = ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, PROPVARIANT*, int>)(*(nint**)store)[5])(store, &key, &value);
            if (hr < 0) return null;
            try
            {
                return value.vt == VT_LPWSTR && value.p != 0 ? Marshal.PtrToStringUni(value.p) : null;
            }
            finally
            {
                Shell32.PropVariantClear(&value);
            }
        }
        finally
        {
            Marshal.Release(store);
        }
    }
}
