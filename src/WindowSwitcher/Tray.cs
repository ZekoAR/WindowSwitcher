using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>The notification-area icon: left click opens Settings, right click shows Settings / Exit.</summary>
internal static unsafe class Tray
{
    const uint IconId = 1;
    const int CommandSettings = 1;
    const int CommandExit = 2;

    static nint s_hwnd;
    static nint s_icon;
    static uint s_taskbarCreated;
    static string s_tip = "";

    public static void Add(nint hwnd, HotkeySettings hotkey)
    {
        s_hwnd = hwnd;
        s_icon = Icons.AppIcon(LIM_SMALL);
        s_taskbarCreated = User32.RegisterWindowMessageW("TaskbarCreated");
        s_tip = Tip(hotkey);
        AddIcon();
    }

    public static void Update(HotkeySettings hotkey)
    {
        s_tip = Tip(hotkey);
        Notify(NIM_MODIFY);
    }

    public static void Remove()
    {
        var data = Data();
        Shell32.Shell_NotifyIconW(NIM_DELETE, &data);
    }

    /// <summary>Handles the icon's callback message and Explorer restarts; false for anything else.</summary>
    public static bool HandleMessage(uint msg, nuint wParam, nint lParam)
    {
        if (s_taskbarCreated != 0 && msg == s_taskbarCreated)
        {
            AddIcon();
            return true;
        }
        if (msg != App.WM_TRAY) return false;

        // NOTIFYICON_VERSION_4: the event is in lParam's low word, the anchor point in wParam.
        switch ((uint)(lParam & 0xFFFF))
        {
            case WM_CONTEXTMENU:
                ShowMenu(LoWord((nint)wParam), HiWord((nint)wParam));
                break;
            case NIN_SELECT:
            case NIN_KEYSELECT:
                SettingsWindow.Show();
                break;
        }
        return true;
    }

    static string Tip(HotkeySettings hotkey) => $"WindowSwitcher - {hotkey} + right mouse button";

    static void AddIcon()
    {
        if (!Notify(NIM_ADD))
            Log.Write("Shell_NotifyIcon(NIM_ADD) failed");
        var data = Data();
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell32.Shell_NotifyIconW(NIM_SETVERSION, &data);
    }

    static NOTIFYICONDATAW Data() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = s_hwnd,
        uID = IconId,
    };

    static bool Notify(uint message)
    {
        var data = Data();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.uCallbackMessage = App.WM_TRAY;
        data.hIcon = s_icon;
        int length = Math.Min(s_tip.Length, 127);
        for (int i = 0; i < length; i++) data.szTip[i] = s_tip[i];
        data.szTip[length] = '\0';
        return Shell32.Shell_NotifyIconW(message, &data);
    }

    static void ShowMenu(int x, int y)
    {
        nint menu = User32.CreatePopupMenu();
        try
        {
            User32.AppendMenuW(menu, MF_STRING, CommandSettings, "Settings...");
            User32.AppendMenuW(menu, MF_SEPARATOR, 0, null);
            User32.AppendMenuW(menu, MF_STRING, CommandExit, "Exit");

            // The owner must be foreground or the menu does not close when the user clicks elsewhere;
            // the mask key makes sure Windows allows that.
            InputHook.SendMaskKey();
            User32.SetForegroundWindow(s_hwnd);
            int command = User32.TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY | TPM_BOTTOMALIGN,
                x, y, s_hwnd, 0);
            User32.PostMessageW(s_hwnd, WM_NULL, 0, 0);

            switch (command)
            {
                case CommandSettings:
                    SettingsWindow.Show();
                    break;
                case CommandExit:
                    User32.PostMessageW(s_hwnd, WM_CLOSE, 0, 0);
                    break;
            }
        }
        finally
        {
            User32.DestroyMenu(menu);
        }
    }
}
