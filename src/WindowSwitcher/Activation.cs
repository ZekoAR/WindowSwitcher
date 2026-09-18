using System.Diagnostics;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// Brings a window to the front (restoring it if minimized), makes sure it has the keyboard focus, and,
/// when the settings ask for it, centers the pointer on it.
/// </summary>
internal static class Activation
{
    const int FocusWaitMs = 100;

    /// <summary>How the last activation got the window to the front: "direct" or "attach".</summary>
    public static string LastRoute { get; private set; } = "";

    /// <summary>Whether the last activated window's thread ended up with a focused window.</summary>
    public static bool LastFocused { get; private set; }

    /// <param name="movePointer">Whether to put the pointer in the middle of the window (Settings.MovePointerToWindow).</param>
    public static bool Activate(nint hwnd, bool movePointer)
    {
        LastRoute = "";
        LastFocused = false;
        if (!User32.IsWindow(hwnd)) return false;
        if (User32.IsIconic(hwnd))
            User32.ShowWindow(hwnd, SW_RESTORE);

        // Windows only lets a background process set the foreground window if it provided the last
        // input; the mask key makes that true without a keystroke any app reacts to.
        InputHook.SendMaskKey();
        User32.SetForegroundWindow(hwnd);
        LastRoute = "direct";
        bool ok = IsForeground(hwnd);
        if (!ok)
        {
            LastRoute = "attach";
            ok = ForceForeground(hwnd);
        }
        LastFocused = ok && EnsureFocus(hwnd);

        if (movePointer) CenterPointer(hwnd);
        return ok;
    }

    /// <summary>A window with an open dialog hands the foreground to that dialog; both count.</summary>
    static bool IsForeground(nint hwnd)
    {
        nint foreground = User32.GetForegroundWindow();
        return foreground == hwnd || (foreground != 0 && SameRoot(foreground, hwnd));
    }

    static bool SameRoot(nint a, nint b) =>
        User32.GetAncestor(a, GA_ROOTOWNER) == User32.GetAncestor(b, GA_ROOTOWNER);

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

    /// <summary>
    /// Being in front is not the same as having the keyboard: the window's thread must also have a focused
    /// window, or typing goes nowhere. The window normally sets its own focus as it handles the
    /// activation, which happens on its thread, so it gets a moment first. Only if it has none after that
    /// is the focus given to the window itself; its own focus handling passes it on to the right control.
    /// </summary>
    static bool EnsureFocus(nint hwnd)
    {
        uint thread = User32.GetWindowThreadProcessId(hwnd, out _);
        long deadline = Stopwatch.GetTimestamp() + FocusWaitMs * Stopwatch.Frequency / 1000;
        while (true)
        {
            if (HasFocus(thread, hwnd)) return true;
            if (Stopwatch.GetTimestamp() >= deadline) break;
            Thread.Sleep(5);
        }

        uint self = Kernel32.GetCurrentThreadId();
        bool attached = thread != self && User32.AttachThreadInput(self, thread, true);
        try
        {
            User32.SetFocus(hwnd);
        }
        finally
        {
            if (attached) User32.AttachThreadInput(self, thread, false);
        }
        bool focused = HasFocus(thread, hwnd);
        Log.Write($"window {hwnd:X} had no keyboard focus after {FocusWaitMs} ms; gave it the focus: {(focused ? "ok" : "failed")}");
        return focused;
    }

    static unsafe bool HasFocus(uint thread, nint hwnd)
    {
        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        return User32.GetGUIThreadInfo(thread, &info)
            && info.hwndFocus != 0
            && info.hwndActive != 0
            && SameRoot(info.hwndActive, hwnd);
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
