using System.Runtime.InteropServices;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// The background app: a hidden main window that owns the tray icon and receives the hook's gesture
/// messages, the popup, and the UI thread's message loop.
/// </summary>
internal static unsafe class App
{
    public const uint WM_GESTURE_START = WM_APP + 1;
    public const uint WM_GESTURE_MOVE = WM_APP + 2;
    public const uint WM_GESTURE_END = WM_APP + 3;
    public const uint WM_THUMBNAIL = WM_APP + 4;
    public const uint WM_GESTURE_START_ALL = WM_APP + 5;
    public const uint WM_GESTURE_CANCEL = WM_APP + 6;
    public const uint WM_TRAY = WM_APP + 10;

    const string MainClassName = "WindowSwitcher.Main";
    const string Caption = "WindowSwitcher";

    static nint s_main;
    static Popup? s_popup;
    static Task<CaptureService>? s_capture;

    public static nint Instance { get; private set; }
    public static Settings Settings { get; private set; } = new();

    public static int Run()
    {
        using var single = new Mutex(true, @"Local\WindowSwitcher.SingleInstance", out bool first);
        if (!first)
        {
            User32.MessageBoxW(0, "WindowSwitcher is already running. Its icon is in the notification area.", Caption, MB_OK);
            return 0;
        }

        Instance = Kernel32.GetModuleHandleW(null);
        Settings = Settings.Load(out string? settingsError);
        if (settingsError is not null)
            User32.MessageBoxW(0, settingsError, Caption, MB_OK | MB_ICONWARNING);

        if (!CaptureService.IsSupported)
        {
            User32.MessageBoxW(0, "Windows Graphics Capture is not available on this system.", Caption, MB_OK | MB_ICONERROR);
            return 1;
        }

        // The D3D device and the borderless-capture request are made once, off the UI thread.
        s_capture = Task.Run(CaptureService.CreateAsync);

        s_main = CreateMainWindow();
        s_popup = new Popup(Instance, s_main);
        Flash.Init(Instance);
        Tray.Add(s_main, Settings);
        try
        {
            InputHook.Start(s_main, Settings);
        }
        catch (Exception e)
        {
            Log.Error("installing the mouse hook", e);
            User32.MessageBoxW(0, $"WindowSwitcher could not install its mouse hook.\n\n{e.Message}", Caption, MB_OK | MB_ICONERROR);
            User32.DestroyWindow(s_main);
            return 1;
        }

        MSG msg;
        while (User32.GetMessageW(&msg, 0, 0, 0) > 0)
        {
            nint settings = SettingsWindow.Handle;
            if (settings != 0 && User32.IsDialogMessageW(settings, &msg)) continue;
            User32.TranslateMessage(&msg);
            User32.DispatchMessageW(&msg);
        }

        InputHook.Stop();
        s_popup.Hide();
        if (s_capture.IsCompletedSuccessfully) s_capture.Result.Dispose();
        return 0;
    }

    public static void ApplySettings(Settings settings)
    {
        Settings = settings;
        InputHook.SetHotkeys(settings);
        Tray.Update(settings);
    }

    /// <summary>The popup saw the right-button release itself, so the hook did not end the gesture.</summary>
    public static void EndGestureWithoutHook()
    {
        InputHook.Reset();
        User32.GetCursorPos(out POINT pt);
        EndGesture(pt.X, pt.Y);
    }

    static nint CreateMainWindow()
    {
        nint hwnd;
        fixed (char* cls = MainClassName)
        fixed (char* caption = Caption)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &MainWndProc,
                hInstance = Instance,
                lpszClassName = cls,
            };
            if (User32.RegisterClassExW(&wc) == 0)
                throw new InvalidOperationException($"RegisterClassEx(main) failed: {Marshal.GetLastPInvokeError()}");
            // A hidden top-level window rather than a message-only one: only top-level windows receive
            // the TaskbarCreated broadcast that says the tray icon must be added again.
            hwnd = User32.CreateWindowExW(WS_EX_TOOLWINDOW, cls, caption, WS_POPUP, 0, 0, 0, 0, 0, 0, Instance, 0);
        }
        if (hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx(main) failed: {Marshal.GetLastPInvokeError()}");
        return hwnd;
    }

    static void StartGesture(int x, int y, bool allMonitors)
    {
        InputHook.SendMaskKey();
        s_popup!.Open(x, y, allMonitors, Settings, s_capture!);
    }

    static void EndGesture(int x, int y)
    {
        nint target = s_popup!.Close(x, y);
        if (target == 0) return;
        bool ok = Activation.Activate(target);
        Flash.Start(target, s_popup.Accent, Settings.SwitchFlash);
        s_popup.HideOverlays();
        s_popup.GateLine($"activated {target:X} foreground {(ok ? 1 : 0)} route {Activation.LastRoute} focus {(Activation.LastFocused ? 1 : 0)}");
        if (!ok) Log.Write($"could not bring window {target:X} to the foreground");
    }

    [UnmanagedCallersOnly]
    static nint MainWndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_GESTURE_START:
                case WM_GESTURE_START_ALL:
                    StartGesture((int)(uint)wParam, (int)lParam, allMonitors: msg == WM_GESTURE_START_ALL);
                    return 0;
                case WM_GESTURE_MOVE:
                    InputHook.MoveHandled();
                    POINT p = InputHook.LastPoint;
                    s_popup?.GateLine($"move {p.X} {p.Y}");
                    s_popup?.Hover(p.X, p.Y);
                    return 0;
                case WM_GESTURE_END:
                    EndGesture((int)(uint)wParam, (int)lParam);
                    return 0;
                case WM_THUMBNAIL:
                    s_popup?.DrainResults();
                    return 0;
                case WM_GESTURE_CANCEL:
                    s_popup?.Cancel();
                    return 0;
                case WM_CLOSE:
                    User32.DestroyWindow(hwnd);
                    return 0;
                case WM_DESTROY:
                    if (SettingsWindow.Handle != 0) User32.DestroyWindow(SettingsWindow.Handle);
                    Tray.Remove();
                    User32.PostQuitMessage(0);
                    return 0;
            }
            if (Tray.HandleMessage(msg, wParam, lParam)) return 0;
        }
        catch (Exception e)
        {
            Log.Error($"main window message 0x{msg:X}", e);
        }
        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
