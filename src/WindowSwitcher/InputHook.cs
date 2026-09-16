using System.Runtime.InteropServices;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// The gesture: a global low-level mouse hook on its own thread. With exactly the configured modifiers
/// held, right-button down starts the gesture and right-button up ends it; both are swallowed so the
/// window under the pointer never sees a right-click. Moves pass through and are forwarded while the
/// gesture is active. Windows silently removes a low-level hook that answers too slowly, so the
/// callback only reads key state and posts messages.
/// </summary>
internal static unsafe class InputHook
{
    const int ModCtrl = 1, ModShift = 2, ModAlt = 4, ModWin = 8;

    static nint s_hook;
    static uint s_threadId;
    static Thread? s_thread;
    static nint s_target;
    static volatile int s_modifiers;
    static int s_active;
    static int s_movePending;
    static long s_lastPoint;

    public static void Start(nint target, HotkeySettings hotkey)
    {
        s_target = target;
        SetHotkey(hotkey);
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
                User32.TranslateMessage(&msg);
                User32.DispatchMessageW(&msg);
            }
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

    public static void SetHotkey(HotkeySettings hotkey) =>
        s_modifiers = (hotkey.Ctrl ? ModCtrl : 0) | (hotkey.Shift ? ModShift : 0)
            | (hotkey.Alt ? ModAlt : 0) | (hotkey.Win ? ModWin : 0);

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
    public static void Reset() => Interlocked.Exchange(ref s_active, 0);

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
                    if (s_active == 0 && s_modifiers != 0 && HeldModifiers() == s_modifiers)
                    {
                        s_active = 1;
                        SetLastPoint(x, y);
                        User32.PostMessageW(s_target, App.WM_GESTURE_START, (nuint)(uint)x, y);
                        return 1;
                    }
                    break;

                case WM_RBUTTONUP:
                    if (Interlocked.Exchange(ref s_active, 0) == 1)
                    {
                        SetLastPoint(x, y);
                        User32.PostMessageW(s_target, App.WM_GESTURE_END, (nuint)(uint)x, y);
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
}
