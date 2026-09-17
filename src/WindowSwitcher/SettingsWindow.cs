using System.Runtime.InteropServices;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// The settings window: the two key combinations that start the gesture (this monitor, all monitors),
/// the thumbnail height, how much the rest of the screen is dimmed, how a switch is marked, and how the
/// rows are ordered.
/// </summary>
internal static unsafe class SettingsWindow
{
    const string ClassName = "WindowSwitcher.Settings";
    const string Title = "WindowSwitcher settings";
    const uint Style = WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
    const uint ExStyle = 0;
    const int ClientWidth = 400;
    const int ClientHeight = 284;
    const int ControlsX = 148;
    const uint DEFAULT_CHARSET = 1;
    const uint BS_AUTORADIOBUTTON = 0x9;

    // Control ids: this monitor 101-104, all monitors 111-114 (Ctrl, Shift, Alt, Win), height 105,
    // dim 106, switch flash 121-123 (None, Border, Window), row order 131-132 (Fixed, Horizontal).
    const int IdThisMonitor = 101;
    const int IdAllMonitors = 111;
    const int IdHeight = 105;
    const int IdDim = 106;
    const int IdFlash = 121;
    const int IdRowOrder = 131;

    static readonly SwitchFlash[] FlashOrder = [SwitchFlash.None, SwitchFlash.Border, SwitchFlash.Window];
    static readonly RowOrder[] RowOrders = [RowOrder.Fixed, RowOrder.Horizontal];

    static bool s_registered;
    static nint s_font;
    static nint s_prompt, s_thisLabel, s_allLabel;
    static readonly nint[] s_thisKeys = new nint[4];
    static readonly nint[] s_allKeys = new nint[4];
    static nint s_heightLabel, s_height, s_heightUnit;
    static nint s_dimLabel, s_dim, s_dimUnit;
    static nint s_flashLabel;
    static readonly nint[] s_flash = new nint[3];
    static nint s_rowOrderLabel;
    static readonly nint[] s_rowOrder = new nint[2];
    static nint s_save, s_cancel;

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
        s_thisLabel = Control("STATIC", "This monitor:", WS_GROUP, 0);
        CreateKeyRow(s_thisKeys, IdThisMonitor);
        s_allLabel = Control("STATIC", "All monitors:", WS_GROUP, 0);
        CreateKeyRow(s_allKeys, IdAllMonitors);

        s_heightLabel = Control("STATIC", "Thumbnail height:", WS_GROUP, 0);
        s_height = Control("EDIT", "", ES_NUMBER | ES_AUTOHSCROLL | WS_TABSTOP | WS_GROUP, IdHeight, WS_EX_CLIENTEDGE);
        s_heightUnit = Control("STATIC", $"px at 100% scaling ({Settings.MinThumbnailHeight}-{Settings.MaxThumbnailHeight})", 0, 0);
        User32.SendMessageW(s_height, EM_SETLIMITTEXT, 3, 0);

        s_dimLabel = Control("STATIC", "Dim everything else:", WS_GROUP, 0);
        s_dim = Control("EDIT", "", ES_NUMBER | ES_AUTOHSCROLL | WS_TABSTOP | WS_GROUP, IdDim, WS_EX_CLIENTEDGE);
        s_dimUnit = Control("STATIC", $"% (0 = off, at most {Settings.MaxDimPercent})", 0, 0);
        User32.SendMessageW(s_dim, EM_SETLIMITTEXT, 2, 0);

        s_flashLabel = Control("STATIC", "Flash on switch:", WS_GROUP, 0);
        string[] flashNames = ["None", "Border", "Whole window"];
        for (int i = 0; i < s_flash.Length; i++)
            s_flash[i] = Control("BUTTON", flashNames[i], BS_AUTORADIOBUTTON | WS_TABSTOP | (i == 0 ? WS_GROUP : 0), IdFlash + i);

        s_rowOrderLabel = Control("STATIC", "Row order:", WS_GROUP, 0);
        string[] rowOrderNames = ["Fixed", "By horizontal position"];
        for (int i = 0; i < s_rowOrder.Length; i++)
            s_rowOrder[i] = Control("BUTTON", rowOrderNames[i], BS_AUTORADIOBUTTON | WS_TABSTOP | (i == 0 ? WS_GROUP : 0), IdRowOrder + i);

        s_save = Control("BUTTON", "Save", BS_DEFPUSHBUTTON | WS_TABSTOP | WS_GROUP, IDOK);
        s_cancel = Control("BUTTON", "Cancel", BS_PUSHBUTTON | WS_TABSTOP, IDCANCEL);

        Arrange(User32.GetDpiForWindow(Handle));
        Load(App.Settings);

        User32.ShowWindow(Handle, SW_SHOW);
        User32.SetForegroundWindow(Handle);
        User32.SetFocus(s_thisKeys[0]);
    }

    static void CreateKeyRow(nint[] row, int firstId)
    {
        string[] names = ["Ctrl", "Shift", "Alt", "Win"];
        for (int i = 0; i < row.Length; i++)
            row[i] = Control("BUTTON", names[i], BS_AUTOCHECKBOX | WS_TABSTOP | (i == 0 ? WS_GROUP : 0), firstId + i);
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

        // A row: a label on the left, controls from ControlsX.
        void Label(nint label, int y) => Place(label, 16, y + 3, ControlsX - 20, 20);

        void KeyRow(nint label, nint[] keys, int y)
        {
            Label(label, y);
            int[] x = [ControlsX, ControlsX + 64, ControlsX + 128, ControlsX + 184];
            int[] w = [60, 60, 52, 52];
            for (int i = 0; i < keys.Length; i++)
                Place(keys[i], x[i], y, w[i], 22);
        }

        void NumberRow(nint label, nint edit, nint unit, int y)
        {
            Label(label, y);
            Place(edit, ControlsX, y, 56, 24);
            Place(unit, ControlsX + 64, y + 3, ClientWidth - ControlsX - 64 - 12, 20);
        }

        Place(s_prompt, 16, 14, ClientWidth - 32, 20);
        KeyRow(s_thisLabel, s_thisKeys, 40);
        KeyRow(s_allLabel, s_allKeys, 68);
        NumberRow(s_heightLabel, s_height, s_heightUnit, 106);
        NumberRow(s_dimLabel, s_dim, s_dimUnit, 136);
        Label(s_flashLabel, 168);
        Place(s_flash[0], ControlsX, 168, 52, 22);
        Place(s_flash[1], ControlsX + 56, 168, 64, 22);
        Place(s_flash[2], ControlsX + 124, 168, 116, 22);
        Label(s_rowOrderLabel, 198);
        Place(s_rowOrder[0], ControlsX, 198, 56, 22);
        Place(s_rowOrder[1], ControlsX + 56, 198, 184, 22);
        Place(s_save, ClientWidth - 16 - 88 - 8 - 88, ClientHeight - 16 - 28, 88, 28);
        Place(s_cancel, ClientWidth - 16 - 88, ClientHeight - 16 - 28, 88, 28);
    }

    static void Load(Settings settings)
    {
        LoadRow(s_thisKeys, settings.Hotkey);
        LoadRow(s_allKeys, settings.AllMonitorsHotkey);
        User32.SetWindowTextW(s_height, settings.ThumbnailHeight.ToString());
        User32.SetWindowTextW(s_dim, settings.DimPercent.ToString());
        for (int i = 0; i < s_flash.Length; i++)
            SetChecked(s_flash[i], FlashOrder[i] == settings.SwitchFlash);
        for (int i = 0; i < s_rowOrder.Length; i++)
            SetChecked(s_rowOrder[i], RowOrders[i] == settings.RowOrder);
    }

    static void LoadRow(nint[] keys, HotkeySettings hotkey)
    {
        SetChecked(keys[0], hotkey.Ctrl);
        SetChecked(keys[1], hotkey.Shift);
        SetChecked(keys[2], hotkey.Alt);
        SetChecked(keys[3], hotkey.Win);
    }

    static HotkeySettings ReadRow(nint[] keys) => new()
    {
        Ctrl = IsChecked(keys[0]),
        Shift = IsChecked(keys[1]),
        Alt = IsChecked(keys[2]),
        Win = IsChecked(keys[3]),
    };

    static void SetChecked(nint button, bool value) => User32.SendMessageW(button, BM_SETCHECK, value ? 1u : 0u, 0);

    static bool IsChecked(nint button) => User32.SendMessageW(button, BM_GETCHECK, 0, 0) == 1;

    static bool ReadNumber(nint edit, int min, int max, string what, out int value)
    {
        if (int.TryParse(User32.GetText(edit), out value) && value >= min && value <= max) return true;
        User32.MessageBoxW(Handle, $"Enter {what} from {min} to {max}.", "WindowSwitcher", MB_OK | MB_ICONWARNING);
        User32.SetFocus(edit);
        return false;
    }

    static void Save()
    {
        HotkeySettings thisMonitor = ReadRow(s_thisKeys);
        HotkeySettings allMonitors = ReadRow(s_allKeys);
        if (thisMonitor.IsEmpty || allMonitors.IsEmpty)
        {
            User32.MessageBoxW(Handle, "Choose at least one key in each row. Without one, every right-click would open WindowSwitcher.",
                "WindowSwitcher", MB_OK | MB_ICONWARNING);
            User32.SetFocus(thisMonitor.IsEmpty ? s_thisKeys[0] : s_allKeys[0]);
            return;
        }
        if (thisMonitor.SameKeys(allMonitors))
        {
            User32.MessageBoxW(Handle, "The two rows need different keys.", "WindowSwitcher", MB_OK | MB_ICONWARNING);
            User32.SetFocus(s_allKeys[0]);
            return;
        }
        if (!ReadNumber(s_height, Settings.MinThumbnailHeight, Settings.MaxThumbnailHeight, "a thumbnail height", out int height))
            return;
        if (!ReadNumber(s_dim, 0, Settings.MaxDimPercent, "a dimming percentage", out int dim))
            return;
        SwitchFlash flash = SwitchFlash.Border;
        for (int i = 0; i < s_flash.Length; i++)
            if (IsChecked(s_flash[i])) flash = FlashOrder[i];
        RowOrder rowOrder = RowOrder.Fixed;
        for (int i = 0; i < s_rowOrder.Length; i++)
            if (IsChecked(s_rowOrder[i])) rowOrder = RowOrders[i];

        var settings = new Settings
        {
            Hotkey = thisMonitor,
            AllMonitorsHotkey = allMonitors,
            ThumbnailHeight = height,
            DimPercent = dim,
            SwitchFlash = flash,
            RowOrder = rowOrder,
        };
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
