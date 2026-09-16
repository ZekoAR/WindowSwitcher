using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>Brings a window to the front (restoring it if minimized) and centers the pointer on it.</summary>
internal static class Activation
{
    const uint GA_ROOTOWNER = 3;

    public static bool Activate(nint hwnd)
    {
        if (!User32.IsWindow(hwnd)) return false;
        if (User32.IsIconic(hwnd))
            User32.ShowWindow(hwnd, SW_RESTORE);

        // Windows only lets a background process set the foreground window if it provided the last
        // input; the mask key makes that true without a keystroke any app reacts to.
        InputHook.SendMaskKey();
        User32.SetForegroundWindow(hwnd);
        bool ok = IsForeground(hwnd) || ForceForeground(hwnd);

        CenterPointer(hwnd);
        return ok;
    }

    /// <summary>A window with an open dialog hands the foreground to that dialog; both count.</summary>
    static bool IsForeground(nint hwnd)
    {
        nint foreground = User32.GetForegroundWindow();
        return foreground == hwnd
            || (foreground != 0 && User32.GetAncestor(foreground, GA_ROOTOWNER) == User32.GetAncestor(hwnd, GA_ROOTOWNER));
    }

    static bool ForceForeground(nint hwnd)
    {
        nint foreground = User32.GetForegroundWindow();
        uint foregroundThread = foreground != 0 ? User32.GetWindowThreadProcessId(foreground, out _) : 0;
        uint self = Kernel32.GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != self
            && User32.AttachThreadInput(self, foregroundThread, true);
        try
        {
            User32.BringWindowToTop(hwnd);
            User32.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) User32.AttachThreadInput(self, foregroundThread, false);
        }
        return IsForeground(hwnd);
    }

    static unsafe void CenterPointer(nint hwnd)
    {
        RECT r;
        if (Dwm.DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &r, sizeof(RECT)) < 0)
            User32.GetWindowRect(hwnd, out r);
        int x = r.Left + r.Width / 2;
        int y = r.Top + r.Height / 2;
        User32.SetCursorPos(x, y);
        InputHook.SetLastPoint(x, y);
    }
}
