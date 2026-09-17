using System.Runtime.InteropServices;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// Solid-color, click-through, topmost windows: layered (so mouse input passes through) with a constant
/// alpha, painted with the brush stored in their GWLP_USERDATA. Each user registers its own window
/// class, so the gate can tell them apart.
/// </summary>
internal static unsafe class SolidWindows
{
    public static void Register(nint instance, string className)
    {
        fixed (char* cls = className)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = instance,
                lpszClassName = cls,
            };
            if (User32.RegisterClassExW(&wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx({className}) failed: {Marshal.GetLastPInvokeError()}");
        }
    }

    public static nint Create(nint instance, string className, byte alpha)
    {
        nint hwnd;
        fixed (char* cls = className)
            hwnd = User32.CreateWindowExW(
                WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT,
                cls, cls, WS_POPUP, 0, 0, 1, 1, 0, 0, instance, 0);
        if (hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx({className}) failed: {Marshal.GetLastPInvokeError()}");
        User32.SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
        return hwnd;
    }

    /// <summary>Makes each window paint with <paramref name="brush"/> (the caller owns the brush).</summary>
    public static void SetBrush(ReadOnlySpan<nint> windows, nint brush)
    {
        foreach (nint hwnd in windows)
        {
            User32.SetWindowLongPtrW(hwnd, GWLP_USERDATA, brush);
            User32.InvalidateRect(hwnd, null, true);
        }
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                User32.GetClientRect(hwnd, out RECT rc);
                User32.FillRect((nint)wParam, &rc, User32.GetWindowLongPtrW(hwnd, GWLP_USERDATA));
                return 1;
            case WM_PAINT:
                PAINTSTRUCT ps;
                User32.BeginPaint(hwnd, &ps);
                User32.EndPaint(hwnd, &ps);
                return 0;
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;
            case WM_DPICHANGED:
                return 0; // placed in physical pixels already
        }
        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}

/// <summary>A rectangular outline made of four thin solid windows (see <see cref="SolidWindows"/>).</summary>
internal sealed class EdgeFrame
{
    readonly nint[] _edges = new nint[4];
    nint _brush;
    uint _color = 0xFFFFFFFF;

    public EdgeFrame(nint instance, string className)
    {
        SolidWindows.Register(instance, className);
        for (int i = 0; i < _edges.Length; i++)
            _edges[i] = SolidWindows.Create(instance, className, 255);
    }

    public void SetColor(uint color)
    {
        if (color == _color) return;
        nint previous = _brush;
        _brush = Gdi32.CreateSolidBrush(color);
        _color = color;
        SolidWindows.SetBrush(_edges, _brush);
        if (previous != 0) Gdi32.DeleteObject(previous);
    }

    /// <summary>Lays the outline along the inside of <paramref name="r"/>, directly below <paramref name="insertAfter"/>.</summary>
    public void Place(RECT r, int thickness, nint insertAfter)
    {
        int t = Math.Max(1, Math.Min(thickness, Math.Min(r.Width, r.Height) / 2));
        const uint flags = SWP_NOACTIVATE;
        User32.SetWindowPos(_edges[0], insertAfter, r.Left, r.Top, r.Width, t, flags);
        User32.SetWindowPos(_edges[1], insertAfter, r.Left, r.Bottom - t, r.Width, t, flags);
        User32.SetWindowPos(_edges[2], insertAfter, r.Left, r.Top + t, t, r.Height - 2 * t, flags);
        User32.SetWindowPos(_edges[3], insertAfter, r.Right - t, r.Top + t, t, r.Height - 2 * t, flags);
    }

    /// <summary>Shows or hides the outline; from the UI thread.</summary>
    public void Show(bool visible)
    {
        foreach (nint edge in _edges)
            User32.ShowWindow(edge, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
    }

    /// <summary>Shows or hides the outline; safe from any thread (the UI thread applies it).</summary>
    public void ShowAsync(bool visible)
    {
        foreach (nint edge in _edges)
            User32.ShowWindowAsync(edge, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
    }
}

/// <summary>
/// One translucent solid window over a rectangle (see <see cref="SolidWindows"/>). A layered window keeps
/// an image buffer of its size, so it is shrunk to 1x1 when not in use.
/// </summary>
internal sealed class FillWindow
{
    readonly nint _hwnd;
    nint _brush;
    uint _color = 0xFFFFFFFF;

    public FillWindow(nint instance, string className, byte alpha)
    {
        SolidWindows.Register(instance, className);
        _hwnd = SolidWindows.Create(instance, className, alpha);
    }

    public void SetColor(uint color)
    {
        if (color == _color) return;
        nint previous = _brush;
        _brush = Gdi32.CreateSolidBrush(color);
        _color = color;
        SolidWindows.SetBrush([_hwnd], _brush);
        if (previous != 0) Gdi32.DeleteObject(previous);
    }

    public void Place(RECT r, nint insertAfter) =>
        User32.SetWindowPos(_hwnd, insertAfter, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE);

    public void Show(bool visible) => User32.ShowWindow(_hwnd, visible ? SW_SHOWNOACTIVATE : SW_HIDE);

    public void ShowAsync(bool visible) => User32.ShowWindowAsync(_hwnd, visible ? SW_SHOWNOACTIVATE : SW_HIDE);

    /// <summary>Hides the window and shrinks it to 1x1, so its image buffer is released; safe from any thread.</summary>
    public void ReleaseAsync() =>
        User32.SetWindowPos(_hwnd, 0, 0, 0, 1, 1, SWP_NOACTIVATE | SWP_NOZORDER | SWP_HIDEWINDOW | SWP_ASYNCWINDOWPOS);
}
