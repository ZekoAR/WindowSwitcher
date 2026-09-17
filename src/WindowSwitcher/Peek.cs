using System.Runtime.InteropServices;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// Shows the hovered window as if it were in front, without touching it: a transparent, click-through
/// overlay just below the popup, exactly over the window's visible frame, into which DWM draws a live
/// 1:1 thumbnail of the window (no outline: the owner removed it, 2026-09-17). The real window keeps its
/// z-order, activation, minimized state and Alt-Tab position. A minimized window is shown where it would
/// restore to, from the image DWM keeps of it.
/// DWM's thumbnail of a window is its visible frame, without the invisible resize border (measured: a
/// 1000x700 window with 7 px borders gives a 986x693 source), so the overlay covers the frame, not the
/// window rectangle.
/// </summary>
internal sealed unsafe class Peek
{
    const string ClassName = "WindowSwitcher.Peek";

    readonly nint _hwnd;
    // The invisible resize border around each window's visible frame, measured while it was not
    // minimized; a minimized window cannot be asked. Keyed like WindowOrder: handle and process.
    readonly Dictionary<(nint Hwnd, uint ProcessId), RECT> _insets = [];
    nint _thumbnail;

    public Peek(nint instance)
    {
        fixed (char* cls = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = instance,
                lpszClassName = cls,
            };
            if (User32.RegisterClassExW(&wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx(peek) failed: {Marshal.GetLastPInvokeError()}");
            _hwnd = User32.CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
                cls, cls, WS_POPUP, 0, 0, 1, 1, 0, 0, instance, 0);
        }
        if (_hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx(peek) failed: {Marshal.GetLastPInvokeError()}");

        int corner = DWMWCP_DONOTROUND;
        Dwm.DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        uint noBorder = DWMWA_COLOR_NONE;
        Dwm.DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, &noBorder, sizeof(uint));
        // Same per-pixel transparency as the popup (see Popup's constructor). The overlay paints itself
        // black with GDI, which leaves alpha 0: fully transparent wherever the thumbnail does not draw.
        nint region = Gdi32.CreateRectRgn(0, 0, -1, -1);
        var blur = new DWM_BLURBEHIND { dwFlags = DWM_BB_ENABLE | DWM_BB_BLURREGION, fEnable = 1, hRgnBlur = region };
        Dwm.DwmEnableBlurBehindWindow(_hwnd, &blur);
        Gdi32.DeleteObject(region);
    }

    /// <summary>Remembers the frame insets of the windows that are not minimized, and forgets closed windows.</summary>
    public void Learn(IEnumerable<WindowInfo> windows)
    {
        foreach (var w in windows)
        {
            if (w.Minimized || w.WindowRect.Width <= 0) continue;
            _insets[(w.Hwnd, w.ProcessId)] = new RECT(
                w.Bounds.Left - w.WindowRect.Left, w.Bounds.Top - w.WindowRect.Top,
                w.WindowRect.Right - w.Bounds.Right, w.WindowRect.Bottom - w.Bounds.Bottom);
        }
        List<(nint, uint)>? gone = null;
        foreach (var key in _insets.Keys)
        {
            if (User32.IsWindow(key.Hwnd) && User32.GetWindowThreadProcessId(key.Hwnd, out uint pid) != 0 && pid == key.ProcessId)
                continue;
            (gone ??= []).Add(key);
        }
        if (gone is not null)
            foreach (var key in gone) _insets.Remove(key);
    }

    /// <summary>Shows <paramref name="window"/> in front, just below <paramref name="above"/> (the popup).</summary>
    public void Show(WindowInfo window, nint above)
    {
        Hide();
        nint hwnd = window.Hwnd;
        if (!User32.IsWindow(hwnd)) return;
        if (Dwm.DwmRegisterThumbnail(_hwnd, hwnd, out nint thumbnail) < 0 || thumbnail == 0) return;
        _thumbnail = thumbnail;

        if (!Placement(window, thumbnail, out RECT frame))
        {
            Hide();
            return;
        }

        User32.SetWindowPos(_hwnd, above, frame.Left, frame.Top, frame.Width, frame.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT(0, 0, frame.Width, frame.Height),
            opacity = 255,
            fVisible = 1,
            fSourceClientAreaOnly = 0,
        };
        Dwm.DwmUpdateThumbnailProperties(thumbnail, &props);
    }

    public void Hide()
    {
        if (_thumbnail != 0)
        {
            Dwm.DwmUnregisterThumbnail(_thumbnail);
            _thumbnail = 0;
        }
        User32.ShowWindow(_hwnd, SW_HIDE);
    }

    /// <summary>
    /// The window's visible frame, where the overlay goes. A minimized window's frame is
    /// where it restores to: the restored window rectangle moved in by the invisible border on the left
    /// and top, at the size of the image DWM kept.
    /// </summary>
    bool Placement(WindowInfo window, nint thumbnail, out RECT frame)
    {
        nint hwnd = window.Hwnd;
        frame = default;
        if (!User32.IsIconic(hwnd))
        {
            RECT visible;
            if (Dwm.DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &visible, sizeof(RECT)) >= 0)
                frame = visible;
            else
                User32.GetWindowRect(hwnd, out frame);
            return frame.Width > 0 && frame.Height > 0;
        }

        if (Dwm.DwmQueryThumbnailSourceSize(thumbnail, out SIZE source) < 0 || source.cx <= 0 || source.cy <= 0)
            return false;
        WINDOWPLACEMENT wp = new() { length = (uint)sizeof(WINDOWPLACEMENT) };
        if (!User32.GetWindowPlacement(hwnd, &wp)) return false;
        var (monitorBounds, work, _) = Monitors.Describe(window.Monitor);

        if ((wp.flags & WPF_RESTORETOMAXIMIZED) != 0)
        {
            // A maximized window's visible frame is the work area.
            int x = work.Left + (work.Width - source.cx) / 2;
            int y = work.Top + (work.Height - source.cy) / 2;
            frame = new RECT(x, y, x + source.cx, y + source.cy);
            return true;
        }

        // rcNormalPosition is in workspace coordinates: offset by where the work area starts on its monitor.
        RECT insets = _insets.TryGetValue((hwnd, window.ProcessId), out RECT known) ? known : EstimateInsets(hwnd);
        int left = wp.rcNormalPosition.Left + work.Left - monitorBounds.Left + insets.Left;
        int top = wp.rcNormalPosition.Top + work.Top - monitorBounds.Top + insets.Top;
        frame = new RECT(left, top, left + source.cx, top + source.cy);
        return true;
    }

    /// <summary>
    /// The usual invisible resize border of a sizable window: the sizing frame plus padding, less the
    /// visible 1 px, on the left, right and bottom. Right for every window measured at 100% scaling
    /// (2026-09-17); at 150% some apps differ (7 or 9 against 10), which is why measured insets are preferred.
    /// </summary>
    static RECT EstimateInsets(nint hwnd)
    {
        long style = User32.GetWindowLongPtrW(hwnd, GWL_STYLE);
        if ((style & WS_THICKFRAME) == 0) return default;
        uint dpi = User32.GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        int side = User32.GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi) + User32.GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi) - 1;
        return new RECT(side, 0, side, side);
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                User32.GetClientRect(hwnd, out RECT rc);
                User32.FillRect((nint)wParam, &rc, Gdi32.GetStockObject(BLACK_BRUSH));
                return 1;
            case WM_PAINT:
                PAINTSTRUCT ps;
                User32.BeginPaint(hwnd, &ps);
                User32.EndPaint(hwnd, &ps);
                return 0;
            case WM_NCHITTEST:
                return HTTRANSPARENT;
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;
            case WM_DPICHANGED:
                return 0; // placed in physical pixels already
        }
        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
