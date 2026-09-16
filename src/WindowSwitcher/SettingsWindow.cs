using System.Runtime.InteropServices;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>The settings window: which modifier keys start the gesture, and the thumbnail height.</summary>
internal static unsafe class SettingsWindow
{
    const string ClassName = "WindowSwitcher.Settings";
    const string Title = "WindowSwitcher settings";
    const uint Style = WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
    const uint ExStyle = 0;
    const int ClientWidth = 380;
    const int ClientHeight = 190;
    const uint DEFAULT_CHARSET = 1;

    static bool s_registered;
    static nint s_font;
    static nint s_prompt, s_ctrl, s_shift, s_alt, s_win;
    static nint s_heightLabel, s_height, s_range, s_save, s_cancel;

    /// <summary>The open settings window, or 0. The message loop gives it dialog keyboard handling.</summary>
    public static nint Handle { get; private set; }

    public static void Show()
    {
        if (Handle != 0)
        {
            User32.ShowWindow(Handle, SW_RESTORE);
            User32.SetForegroundWindow(Handle);
            return;
        }
        Register();

        User32.GetCursorPos(out POINT pt);
        var (_, work, dpi) = Monitors.Describe(User32.MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST));
        double scale = dpi / 96.0;
        var outer = new RECT(0, 0, (int)Math.Round(ClientWidth * scale), (int)Math.Round(ClientHeight * scale));
        User32.AdjustWindowRectExForDpi(&outer, Style, false, ExStyle, dpi);
        int x = work.Left + (work.Width - outer.Width) / 2;
        int y = work.Top + (work.Height - outer.Height) / 2;

        fixed (char* cls = ClassName)
        fixed (char* title = Title)
            Handle = User32.CreateWindowExW(ExStyle, cls, title, Style, x, y, outer.Width, outer.Height, 0, 0, App.Instance, 0);
        if (Handle == 0)
        {
            Log.Write($"CreateWindowEx(settings) failed: {Marshal.GetLastPInvokeError()}");
            return;
        }

        s_prompt = Control("STATIC", "Hold these keys and press the right mouse button:", 0, 0);
        s_ctrl = Control("BUTTON", "Ctrl", BS_AUTOCHECKBOX | WS_TABSTOP | WS_GROUP, 101);
        s_shift = Control("BUTTON", "Shift", BS_AUTOCHECKBOX | WS_TABSTOP, 102);
        s_alt = Control("BUTTON", "Alt", BS_AUTOCHECKBOX | WS_TABSTOP, 103);
        s_win = Control("BUTTON", "Win", BS_AUTOCHECKBOX | WS_TABSTOP, 104);
        s_heightLabel = Control("STATIC", "Thumbnail height at 100% display scaling (pixels):", WS_GROUP, 0);
        s_height = Control("EDIT", "", ES_NUMBER | ES_AUTOHSCROLL | WS_TABSTOP | WS_GROUP, 105, WS_EX_CLIENTEDGE);
        s_range = Control("STATIC", $"{Settings.MinThumbnailHeight} - {Settings.MaxThumbnailHeight}", 0, 0);
        s_save = Control("BUTTON", "Save", BS_DEFPUSHBUTTON | WS_TABSTOP | WS_GROUP, IDOK);
        s_cancel = Control("BUTTON", "Cancel", BS_PUSHBUTTON | WS_TABSTOP, IDCANCEL);
        User32.SendMessageW(s_height, EM_SETLIMITTEXT, 3, 0);

        Arrange(User32.GetDpiForWindow(Handle));
        Load(App.Settings);

        User32.ShowWindow(Handle, SW_SHOW);
        User32.SetForegroundWindow(Handle);
        User32.SetFocus(s_ctrl);
    }

    static void Register()
    {
        if (s_registered) return;
        fixed (char* cls = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = App.Instance,
                hIcon = Icons.AppIcon(LIM_LARGE),
                hIconSm = Icons.AppIcon(LIM_SMALL),
                hCursor = User32.LoadCursorW(0, IDC_ARROW),
                hbrBackground = User32.GetSysColorBrush(COLOR_WINDOW),
                lpszClassName = cls,
            };
            User32.RegisterClassExW(&wc);
        }
        s_registered = true;
    }

    static nint Control(string cls, string text, uint style, int id, uint exStyle = 0)
    {
        fixed (char* c = cls)
        fixed (char* t = text)
            return User32.CreateWindowExW(exStyle, c, t, WS_CHILD | WS_VISIBLE | style, 0, 0, 0, 0, Handle, id, App.Instance, 0);
    }

    static void Arrange(uint dpi)
    {
        double scale = dpi / 96.0;
        int S(int v) => (int)Math.Round(v * scale);

        if (s_font != 0) Gdi32.DeleteObject(s_font);
        s_font = Gdi32.CreateFontW(-S(12), 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET, 0, 0, CLEARTYPE_QUALITY, 0, "Segoe UI");

        void Place(nint control, int x, int y, int w, int h)
        {
            User32.SetWindowPos(control, 0, S(x), S(y), S(w), S(h), SWP_NOZORDER | SWP_NOACTIVATE);
            User32.SendMessageW(control, WM_SETFONT, (nuint)s_font, 1);
        }

        Place(s_prompt, 16, 14, ClientWidth - 32, 20);
        Place(s_ctrl, 16, 40, 64, 22);
        Place(s_shift, 88, 40, 64, 22);
        Place(s_alt, 160, 40, 56, 22);
        Place(s_win, 224, 40, 56, 22);
        Place(s_heightLabel, 16, 78, ClientWidth - 32, 20);
        Place(s_height, 16, 102, 72, 24);
        Place(s_range, 96, 105, 120, 20);
        Place(s_save, ClientWidth - 16 - 88 - 8 - 88, ClientHeight - 16 - 28, 88, 28);
        Place(s_cancel, ClientWidth - 16 - 88, ClientHeight - 16 - 28, 88, 28);
    }

    static void Load(Settings settings)
    {
        SetChecked(s_ctrl, settings.Hotkey.Ctrl);
        SetChecked(s_shift, settings.Hotkey.Shift);
        SetChecked(s_alt, settings.Hotkey.Alt);
        SetChecked(s_win, settings.Hotkey.Win);
        User32.SetWindowTextW(s_height, settings.ThumbnailHeight.ToString());
    }

    static void SetChecked(nint button, bool value) => User32.SendMessageW(button, BM_SETCHECK, value ? 1u : 0u, 0);

    static bool IsChecked(nint button) => User32.SendMessageW(button, BM_GETCHECK, 0, 0) == 1;

    static void Save()
    {
        var hotkey = new HotkeySettings
        {
            Ctrl = IsChecked(s_ctrl),
            Shift = IsChecked(s_shift),
            Alt = IsChecked(s_alt),
            Win = IsChecked(s_win),
        };
        if (hotkey.IsEmpty)
        {
            User32.MessageBoxW(Handle, "Choose at least one key. Without one, every right-click would open WindowSwitcher.",
                "WindowSwitcher", MB_OK | MB_ICONWARNING);
            User32.SetFocus(s_ctrl);
            return;
        }
        if (!int.TryParse(User32.GetText(s_height), out int height)
            || height < Settings.MinThumbnailHeight || height > Settings.MaxThumbnailHeight)
        {
            User32.MessageBoxW(Handle, $"Enter a thumbnail height from {Settings.MinThumbnailHeight} to {Settings.MaxThumbnailHeight}.",
                "WindowSwitcher", MB_OK | MB_ICONWARNING);
            User32.SetFocus(s_height);
            return;
        }

        var settings = new Settings { Hotkey = hotkey, ThumbnailHeight = height };
        try
        {
            settings.Save();
        }
        catch (Exception e)
        {
            User32.MessageBoxW(Handle, $"The settings could not be saved to {Settings.FilePath}.\n\n{e.Message}",
                "WindowSwitcher", MB_OK | MB_ICONERROR);
            return;
        }
        App.ApplySettings(settings);
        User32.DestroyWindow(Handle);
    }

    [UnmanagedCallersOnly]
    static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case WM_COMMAND:
                    switch ((int)(wParam & 0xFFFF))
                    {
                        case IDOK:
                            Save();
                            return 0;
                        case IDCANCEL:
                            User32.DestroyWindow(hwnd);
                            return 0;
                    }
                    break;
                case WM_CTLCOLORSTATIC:
                    Gdi32.SetBkColor((nint)wParam, User32.GetSysColor(COLOR_WINDOW));
                    Gdi32.SetTextColor((nint)wParam, User32.GetSysColor(COLOR_WINDOWTEXT));
                    return User32.GetSysColorBrush(COLOR_WINDOW);
                case WM_DPICHANGED:
                    var suggested = (RECT*)lParam;
                    User32.SetWindowPos(hwnd, 0, suggested->Left, suggested->Top, suggested->Width, suggested->Height,
                        SWP_NOZORDER | SWP_NOACTIVATE);
                    Arrange((uint)(wParam & 0xFFFF));
                    return 0;
                case WM_CLOSE:
                    User32.DestroyWindow(hwnd);
                    return 0;
                case WM_DESTROY:
                    Handle = 0;
                    if (s_font != 0) Gdi32.DeleteObject(s_font);
                    s_font = 0;
                    return 0;
            }
        }
        catch (Exception e)
        {
            Log.Error($"settings message 0x{msg:X}", e);
        }
        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
