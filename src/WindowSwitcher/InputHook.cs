using System.Runtime.InteropServices;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// The gesture: a global low-level mouse hook on its own thread. With exactly one of the two configured
/// key combinations held (this monitor, all monitors), right-button down starts the gesture and
/// right-button up ends it; both are swallowed so the window under the pointer never sees a right-click.
/// Moves pass through and are forwarded while the gesture is active. While a gesture is active, a
/// low-level keyboard hook is installed as well: Esc cancels the gesture and is swallowed, press and
/// release (so the window in front never sees it, and Ctrl+Shift+Esc does not open Task Manager). It is
/// removed again once the gesture and the Esc key are both released, so no keyboard hook exists while
/// idle. Windows silently removes a low-level hook that answers too slowly, so the callbacks only read
/// state and post messages.
/// </summary>
internal static unsafe class InputHook
{
    const int ModCtrl = 1, ModShift = 2, ModAlt = 4, ModWin = 8;
    const uint WM_KEYBOARD_HOOK_ON = WM_APP + 50;
    const uint WM_KEYBOARD_HOOK_OFF = WM_APP + 51;

    static nint s_hook;
    static nint s_keyboardHook;
    static int s_escapeHeld;
    static uint s_threadId;
    static Thread? s_thread;
    static nint s_target;
    static volatile int s_thisMonitor;
    static volatile int s_allMonitors;
    static int s_active;
    static int s_movePending;
    static long s_lastPoint;

    public static void Start(nint target, Settings settings)
    {
        s_target = target;
        SetHotkeys(settings);
        using var ready = new ManualResetEventSlim();
        int error = 0;
        s_thread = new Thread(() =>
        {
            s_threadId = Kernel32.GetCurrentThreadId();
            s_hook = User32.SetWindowsHookExW(WH_MOUSE_LL, &HookProc, Kernel32.GetModuleHandleW(null), 0);
            if (s_hook == 0) error = Marshal.GetLastPInvokeError();
            ready.Set();
            if (s_hook == 0) return;

            MSG msg;
            while (User32.GetMessageW(&msg, 0, 0, 0) > 0)
            {
                switch (msg.message)
                {
                    case WM_KEYBOARD_HOOK_ON:
                        if (s_keyboardHook == 0)
                        {
                            s_keyboardHook = User32.SetWindowsHookExW(WH_KEYBOARD_LL, &KeyboardProc, Kernel32.GetModuleHandleW(null), 0);
                            if (s_keyboardHook == 0) Log.Write($"keyboard hook failed ({Marshal.GetLastPInvokeError()}); Esc will not cancel");
                        }
                        continue;
                    case WM_KEYBOARD_HOOK_OFF:
                        if (s_keyboardHook != 0 && s_active == 0 && s_escapeHeld == 0)
                        {
                            User32.UnhookWindowsHookEx(s_keyboardHook);
                            s_keyboardHook = 0;
                        }
                        continue;
                }
                User32.TranslateMessage(&msg);
                User32.DispatchMessageW(&msg);
            }
            if (s_keyboardHook != 0) User32.UnhookWindowsHookEx(s_keyboardHook);
            s_keyboardHook = 0;
            User32.UnhookWindowsHookEx(s_hook);
            s_hook = 0;
        })
        {
            IsBackground = true,
            Name = "WindowSwitcher input hook",
        };
        s_thread.Start();
        ready.Wait();
        if (error != 0)
            throw new InvalidOperationException($"SetWindowsHookEx failed with error {error}");
    }

    public static void Stop()
    {
        if (s_thread is null) return;
        User32.PostThreadMessageW(s_threadId, WM_QUIT, 0, 0);
        s_thread.Join(2000);
        s_thread = null;
    }

    public static void SetHotkeys(Settings settings)
    {
        s_thisMonitor = Mask(settings.Hotkey);
        s_allMonitors = Mask(settings.AllMonitorsHotkey);
    }

    static int Mask(HotkeySettings hotkey) =>
        (hotkey.Ctrl ? ModCtrl : 0) | (hotkey.Shift ? ModShift : 0) | (hotkey.Alt ? ModAlt : 0) | (hotkey.Win ? ModWin : 0);

    /// <summary>Where the pointer is going: the position of the last mouse event the hook saw.</summary>
    public static POINT LastPoint
    {
        get
        {
            long v = Volatile.Read(ref s_lastPoint);
            return new POINT { X = (int)(v >> 32), Y = (int)v };
        }
    }

    /// <summary>Records a pointer position set by the app itself (SetCursorPos produces no hook event).</summary>
    public static void SetLastPoint(int x, int y) => Volatile.Write(ref s_lastPoint, ((long)x << 32) | (uint)y);

    /// <summary>Called by the UI thread once it has handled a move, so the next move posts again.</summary>
    public static void MoveHandled() => Interlocked.Exchange(ref s_movePending, 0);

    /// <summary>Clears the gesture state when the popup was closed some other way.</summary>
    public static void Reset()
    {
        if (Interlocked.Exchange(ref s_active, 0) == 1)
            User32.PostThreadMessageW(s_threadId, WM_KEYBOARD_HOOK_OFF, 0, 0);
    }

    /// <summary>
    /// Sends an unassigned key press. It cancels modifier-only shortcuts (Win alone opens Start on
    /// release) and makes this process the last input provider, which Windows requires before it lets a
    /// background process set the foreground window.
    /// </summary>
    public static void SendMaskKey()
    {
        INPUT* inputs = stackalloc INPUT[2];
        inputs[0] = new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_MASK_KEY } } };
        inputs[1] = new INPUT { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = VK_MASK_KEY, dwFlags = KEYEVENTF_KEYUP } } };
        User32.SendInput(2, inputs, sizeof(INPUT));
    }

    static int HeldModifiers()
    {
        int held = 0;
        if (User32.GetAsyncKeyState(VK_CONTROL) < 0) held |= ModCtrl;
        if (User32.GetAsyncKeyState(VK_SHIFT) < 0) held |= ModShift;
        if (User32.GetAsyncKeyState(VK_MENU) < 0) held |= ModAlt;
        if (User32.GetAsyncKeyState(VK_LWIN) < 0 || User32.GetAsyncKeyState(VK_RWIN) < 0) held |= ModWin;
        return held;
    }

    [UnmanagedCallersOnly]
    static nint HookProc(int code, nuint wParam, nint lParam)
    {
        if (code == HC_ACTION)
        {
            var info = (MSLLHOOKSTRUCT*)lParam;
            int x = info->pt.X, y = info->pt.Y;
            switch ((uint)wParam)
            {
                case WM_RBUTTONDOWN:
                    if (s_active == 0)
                    {
                        int held = HeldModifiers();
                        // The settings window keeps the two combinations different; this monitor wins a tie.
                        uint start = held == 0 ? 0
                            : held == s_thisMonitor ? App.WM_GESTURE_START
                            : held == s_allMonitors ? App.WM_GESTURE_START_ALL
                            : 0;
                        if (start != 0)
                        {
                            s_active = 1;
                            SetLastPoint(x, y);
                            User32.PostMessageW(s_target, start, (nuint)(uint)x, y);
                            User32.PostThreadMessageW(s_threadId, WM_KEYBOARD_HOOK_ON, 0, 0);
                            return 1;
                        }
                    }
                    break;

                case WM_RBUTTONUP:
                    if (Interlocked.Exchange(ref s_active, 0) == 1)
                    {
                        SetLastPoint(x, y);
                        User32.PostMessageW(s_target, App.WM_GESTURE_END, (nuint)(uint)x, y);
                        User32.PostThreadMessageW(s_threadId, WM_KEYBOARD_HOOK_OFF, 0, 0);
                        return 1;
                    }
                    break;

                case WM_MOUSEMOVE:
                    if (s_active == 1)
                    {
                        SetLastPoint(x, y);
                        if (Interlocked.Exchange(ref s_movePending, 1) == 0)
                            User32.PostMessageW(s_target, App.WM_GESTURE_MOVE, 0, 0);
                    }
                    break;
            }
        }
        return User32.CallNextHookEx(s_hook, code, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    static nint KeyboardProc(int code, nuint wParam, nint lParam)
    {
        if (code == HC_ACTION)
        {
            var key = (KBDLLHOOKSTRUCT*)lParam;
            if (key->vkCode == VK_ESCAPE)
            {
                bool up = (key->flags & LLKHF_UP) != 0;
                if (!up && s_active == 1)
                {
                    // The first press cancels; auto-repeats are swallowed too.
                    if (Interlocked.Exchange(ref s_escapeHeld, 1) == 0)
                        User32.PostMessageW(s_target, App.WM_GESTURE_CANCEL, 0, 0);
                    return 1;
                }
                if (up && Interlocked.Exchange(ref s_escapeHeld, 0) == 1)
                {
                    User32.PostThreadMessageW(s_threadId, WM_KEYBOARD_HOOK_OFF, 0, 0);
                    return 1;
                }
            }
        }
        return User32.CallNextHookEx(s_keyboardHook, code, wParam, lParam);
    }
}
