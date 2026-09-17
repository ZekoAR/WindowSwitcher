using System.Runtime.InteropServices;

namespace WindowSwitcher.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left; Top = top; Right = right; Bottom = bottom;
    }

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public override readonly string ToString() => $"{Left},{Top} {Width}x{Height}";
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X, Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx, cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd;
    public uint message;
    public nuint wParam;
    public nint lParam;
    public uint time;
    public POINT pt;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WNDCLASSEXW
{
    public uint cbSize;
    public uint style;
    public delegate* unmanaged<nint, uint, nuint, nint, nint> lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public char* lpszMenuName;
    public char* lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public uint length;
    public uint flags;
    public uint showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public nuint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public nuint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public nuint dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public nuint dwExtraInfo;
}

[StructLayout(LayoutKind.Explicit)]
internal struct INPUTUNION
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public INPUTUNION u;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PAINTSTRUCT
{
    public nint hdc;
    public int fErase;
    public RECT rcPaint;
    public int fRestore;
    public int fIncUpdate;
    public fixed byte rgbReserved[32];
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFO
{
    public BITMAPINFOHEADER bmiHeader;
    public uint bmiColors;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)] // dwmapi.h is wrapped in pshpack1.h
internal struct DWM_THUMBNAIL_PROPERTIES
{
    public uint dwFlags;
    public RECT rcDestination;
    public RECT rcSource;
    public byte opacity;
    public int fVisible;
    public int fSourceClientAreaOnly;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)] // dwmapi.h is wrapped in pshpack1.h
internal struct DWM_BLURBEHIND
{
    public uint dwFlags;
    public int fEnable;
    public nint hRgnBlur;
    public int fTransitionOnMaximized;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NOTIFYICONDATAW
{
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    public fixed char szTip[128];
    public uint dwState;
    public uint dwStateMask;
    public fixed char szInfo[256];
    public uint uVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GUITHREADINFO
{
    public uint cbSize;
    public uint flags;
    public nint hwndActive;
    public nint hwndFocus;
    public nint hwndCapture;
    public nint hwndMenuOwner;
    public nint hwndMoveSize;
    public nint hwndCaret;
    public RECT rcCaret;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT
{
    public ushort vt;
    public ushort wReserved1, wReserved2, wReserved3;
    public nint p;
    public nint p2;
}

internal static class Win32
{
    // Window messages
    public const uint WM_CREATE = 0x0001;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETFONT = 0x0030;
    public const uint WM_GETICON = 0x007F;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_APP = 0x8000;
    public const uint WM_USER = 0x0400;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_NULL = 0x0000;
    public const uint WM_CTLCOLORSTATIC = 0x0138;
    public const int COLOR_WINDOW = 5;
    public const int COLOR_WINDOWTEXT = 8;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;
    public const int LIM_SMALL = 0;
    public const int LIM_LARGE = 1;
    public const int SM_CXSMICON = 49;

    public const int MA_NOACTIVATE = 3;

    // Window styles
    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_TABSTOP = 0x00010000;
    public const uint WS_GROUP = 0x00020000;
    public const uint WS_BORDER = 0x00800000;

    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const uint LWA_ALPHA = 0x2;
    public const uint GA_ROOTOWNER = 3;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_APPWINDOW = 0x00040000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_EX_CLIENTEDGE = 0x00000200;
    public const uint WS_EX_DLGMODALFRAME = 0x00000001;

    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_VREDRAW = 0x0001;
    public const uint CS_DBLCLKS = 0x0008;
    public const uint CS_DROPSHADOW = 0x00020000;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int GWLP_USERDATA = -21;

    public const uint GW_OWNER = 4;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public static readonly nint HWND_TOPMOST = -1;
    public static readonly nint HWND_MESSAGE = -3;

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint WPF_RESTORETOMAXIMIZED = 0x0002;

    public const uint SMTO_BLOCK = 0x0001;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const nuint ICON_SMALL = 0;
    public const nuint ICON_BIG = 1;
    public const nuint ICON_SMALL2 = 2;
    public const int GCLP_HICON = -14;
    public const int GCLP_HICONSM = -34;
    public const uint DI_NORMAL = 0x0003;

    public const int WH_MOUSE_LL = 14;
    public const int WH_KEYBOARD_LL = 13;
    public const uint LLKHF_UP = 0x80;
    public const uint WM_NCHITTEST = 0x0084;
    public const int HTTRANSPARENT = -1;
    public const int BLACK_BRUSH = 4;
    public const int SM_CXSIZEFRAME = 32;
    public const int SM_CXPADDEDBORDER = 92;
    public const uint WS_THICKFRAME = 0x00040000;
    public const int HC_ACTION = 0;

    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_RETURN = 0x0D;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const ushort VK_MASK_KEY = 0xE8; // unassigned; what AutoHotkey uses as its "menu mask" key

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const uint IMAGE_ICON = 1;
    public const uint LR_DEFAULTCOLOR = 0;
    public const uint LR_SHARED = 0x00008000;
    public static readonly nint IDC_ARROW = 32512;

    public const uint MF_STRING = 0x0000;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_BOTTOMALIGN = 0x0020;

    public const uint MB_OK = 0x0;
    public const uint MB_ICONWARNING = 0x30;
    public const uint MB_ICONERROR = 0x10;

    // Button / edit control styles and messages
    public const uint BS_PUSHBUTTON = 0x0;
    public const uint BS_DEFPUSHBUTTON = 0x1;
    public const uint BS_AUTOCHECKBOX = 0x3;
    public const uint BS_GROUPBOX = 0x7;
    public const uint ES_NUMBER = 0x2000;
    public const uint ES_AUTOHSCROLL = 0x0080;
    public const uint BM_GETCHECK = 0x00F0;
    public const uint BM_SETCHECK = 0x00F1;
    public const uint EM_SETLIMITTEXT = 0x00C5;
    public const uint BN_CLICKED = 0;
    public const int IDOK = 1;
    public const int IDCANCEL = 2;

    // GDI
    public const uint BI_RGB = 0;
    public const uint DIB_RGB_COLORS = 0;
    public const uint SRCCOPY = 0x00CC0020;
    public const int TRANSPARENT = 1;
    public const uint DT_LEFT = 0x0;
    public const uint DT_CENTER = 0x1;
    public const uint DT_VCENTER = 0x4;
    public const uint DT_SINGLELINE = 0x20;
    public const uint DT_NOPREFIX = 0x800;
    public const uint DT_END_ELLIPSIS = 0x8000;
    public const int NULL_PEN = 8;
    public const int NULL_BRUSH = 5;
    public const int PS_SOLID = 0;
    public const int FW_NORMAL = 400;
    public const int FW_SEMIBOLD = 600;
    public const uint CLEARTYPE_QUALITY = 5;
    public const byte AC_SRC_OVER = 0;
    public const byte AC_SRC_ALPHA = 1;

    // DWM
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
    public const uint DWM_BB_ENABLE = 0x1;
    public const uint DWM_BB_BLURREGION = 0x2;
    public const uint DWM_TNP_RECTDESTINATION = 0x1;
    public const uint DWM_TNP_OPACITY = 0x4;
    public const uint DWM_TNP_VISIBLE = 0x8;
    public const uint DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    // Shell notify icon
    public const uint NIM_ADD = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;
    public const uint NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x1;
    public const uint NIF_ICON = 0x2;
    public const uint NIF_TIP = 0x4;
    public const uint NIF_SHOWTIP = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = WM_USER;
    public const uint NIN_KEYSELECT = WM_USER + 1;

    public const int MDT_EFFECTIVE_DPI = 0;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static int LoWord(nint v) => (short)((long)v & 0xFFFF);
    public static int HiWord(nint v) => (short)(((long)v >> 16) & 0xFFFF);
    public static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));
}

internal static unsafe partial class User32
{
    const string Dll = "user32.dll";

    [LibraryImport(Dll, SetLastError = true)]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport(Dll, SetLastError = true)]
    public static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport(Dll)]
    public static partial nint DefWindowProcW(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int cmd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindowAsync(nint hwnd, int cmd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetGUIThreadInfo(uint threadId, GUITHREADINFO* info);

    [LibraryImport(Dll)]
    public static partial int GetMessageW(MSG* msg, nint hwnd, uint min, uint max);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(MSG* msg);

    [LibraryImport(Dll)]
    public static partial nint DispatchMessageW(MSG* msg);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsDialogMessageW(nint hwnd, MSG* msg);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint threadId, uint msg, nuint wParam, nint lParam);

    [LibraryImport(Dll)]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport(Dll)]
    public static partial nint SendMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport(Dll)]
    public static partial nint SendMessageTimeoutW(nint hwnd, uint msg, nuint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);

    [LibraryImport(Dll)]
    public static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport(Dll)]
    public static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport(Dll)]
    public static partial nuint GetClassLongPtrW(nint hwnd, int index);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(nint hwnd, RECT* rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport(Dll)]
    public static partial nint BeginPaint(nint hwnd, PAINTSTRUCT* ps);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EndPaint(nint hwnd, PAINTSTRUCT* ps);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hwnd, out RECT rect);

    [LibraryImport(Dll)]
    public static partial int FillRect(nint hdc, RECT* rect, nint brush);

    [LibraryImport(Dll)]
    public static partial int DrawTextW(nint hdc, char* text, int length, RECT* rect, uint format);

    [LibraryImport(Dll, SetLastError = true)]
    public static partial nint SetWindowsHookExW(int idHook, delegate* unmanaged<int, nuint, nint, nint> proc, nint module, uint threadId);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport(Dll)]
    public static partial nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);

    [LibraryImport(Dll)]
    public static partial short GetAsyncKeyState(int vk);

    [LibraryImport(Dll)]
    public static partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    [LibraryImport(Dll)]
    public static partial nint MonitorFromPoint(POINT pt, uint flags);

    [LibraryImport(Dll)]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(nint hdc, RECT* clip, delegate* unmanaged<nint, nint, RECT*, nint, int> callback, nint param);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(nint monitor, MONITORINFO* info);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint param);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumChildWindows(nint parent, delegate* unmanaged<nint, nint, int> callback, nint param);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport(Dll)]
    public static partial nint GetWindow(nint hwnd, uint cmd);

    [LibraryImport(Dll)]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport(Dll)]
    public static partial nint GetLastActivePopup(nint hwnd);

    [LibraryImport(Dll)]
    public static partial int GetWindowTextLengthW(nint hwnd);

    [LibraryImport(Dll)]
    public static partial int GetWindowTextW(nint hwnd, char* buffer, int max);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowTextW(nint hwnd, string text);

    [LibraryImport(Dll)]
    public static partial int GetClassNameW(nint hwnd, char* buffer, int max);

    [LibraryImport(Dll)]
    public static partial nint GetShellWindow();

    [LibraryImport(Dll)]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowPlacement(nint hwnd, WINDOWPLACEMENT* placement);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawIconEx(nint hdc, int x, int y, nint icon, int cx, int cy, uint step, nint flickerBrush, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport(Dll)]
    public static partial nint LoadImageW(nint instance, nint name, uint type, int cx, int cy, uint flags);

    [LibraryImport(Dll)]
    public static partial nint LoadCursorW(nint instance, nint name);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PrivateExtractIconsW(string file, int index, int cx, int cy, nint* icons, uint* ids, uint count, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport(Dll)]
    public static partial nint GetForegroundWindow();

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(nint hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport(Dll)]
    public static partial nint CreatePopupMenu();

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport(Dll)]
    public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBoxW(nint hwnd, string text, string caption, uint type);

    [LibraryImport(Dll)]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustWindowRectExForDpi(RECT* rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle, uint dpi);

    [LibraryImport(Dll)]
    public static partial nint SetFocus(nint hwnd);

    [LibraryImport(Dll)]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport(Dll)]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport(Dll)]
    public static partial nint GetSysColorBrush(int index);

    [LibraryImport(Dll)]
    public static partial uint GetSysColor(int index);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnableWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [LibraryImport("comctl32.dll")]
    public static partial int LoadIconMetric(nint instance, nint name, int metric, out nint icon);

    /// <summary>A window's text (title, or a control's contents).</summary>
    public static string GetText(nint hwnd)
    {
        int length = GetWindowTextLengthW(hwnd);
        if (length <= 0) return "";
        int size = Math.Min(length + 1, 1024);
        char* buffer = stackalloc char[size];
        int read = GetWindowTextW(hwnd, buffer, size);
        return new string(buffer, 0, read);
    }
}

internal static unsafe partial class Gdi32
{
    const string Dll = "gdi32.dll";

    [LibraryImport(Dll)]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport(Dll)]
    public static partial nint CreateDIBSection(nint hdc, BITMAPINFO* info, uint usage, out byte* bits, nint section, uint offset);

    [LibraryImport(Dll)]
    public static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint obj);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(nint dst, int x, int y, int cx, int cy, nint src, int x1, int y1, uint rop);

    [LibraryImport(Dll)]
    public static partial nint CreateSolidBrush(uint color);

    [LibraryImport(Dll)]
    public static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport(Dll)]
    public static partial nint CreatePen(int style, int width, uint color);

    [LibraryImport(Dll)]
    public static partial nint GetStockObject(int index);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RoundRect(nint hdc, int left, int top, int right, int bottom, int width, int height);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);

    [LibraryImport(Dll)]
    public static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport(Dll)]
    public static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport(Dll)]
    public static partial uint SetBkColor(nint hdc, uint color);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GdiAlphaBlend(nint dst, int xd, int yd, int wd, int hd, nint src, int xs, int ys, int ws, int hs, uint blendFunction);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GdiFlush();

    public static uint Blend(byte constantAlpha, bool perPixel) =>
        (uint)(Win32.AC_SRC_OVER | (constantAlpha << 16) | ((perPixel ? Win32.AC_SRC_ALPHA : 0) << 24));
}

internal static unsafe partial class Dwm
{
    const string Dll = "dwmapi.dll";

    [LibraryImport(Dll)]
    public static partial int DwmGetWindowAttribute(nint hwnd, int attr, void* value, int size);

    [LibraryImport(Dll)]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attr, void* value, int size);

    [LibraryImport(Dll)]
    public static partial int DwmEnableBlurBehindWindow(nint hwnd, DWM_BLURBEHIND* blurBehind);

    [LibraryImport(Dll)]
    public static partial int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

    [LibraryImport(Dll)]
    public static partial int DwmUnregisterThumbnail(nint thumbnail);

    [LibraryImport(Dll)]
    public static partial int DwmUpdateThumbnailProperties(nint thumbnail, DWM_THUMBNAIL_PROPERTIES* properties);

    [LibraryImport(Dll)]
    public static partial int DwmQueryThumbnailSourceSize(nint thumbnail, out SIZE size);
}

internal static unsafe partial class Kernel32
{
    const string Dll = "kernel32.dll";

    [LibraryImport(Dll, SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageNameW(nint process, uint flags, char* buffer, uint* size);

    [LibraryImport(Dll)]
    public static partial uint GetCurrentThreadId();

    [LibraryImport(Dll)]
    public static partial nint GetModuleHandleW(char* name);

    [LibraryImport(Dll, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateMutexW(nint attributes, [MarshalAs(UnmanagedType.Bool)] bool initialOwner, string name);

    public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2;
    public const uint TIMER_ALL_ACCESS = 0x1F0003;
    public const uint INFINITE = 0xFFFFFFFF;

    [LibraryImport(Dll)]
    public static partial nint CreateWaitableTimerExW(nint attributes, char* name, uint flags, uint access);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWaitableTimer(nint timer, long* dueTime, int period, nint completion, nint arg,
        [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport(Dll)]
    public static partial uint WaitForSingleObject(nint handle, uint milliseconds);
}

internal static unsafe partial class Shell32
{
    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    [LibraryImport("shell32.dll")]
    public static partial int SHGetPropertyStoreForWindow(nint hwnd, Guid* iid, nint* store);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(PROPVARIANT* pv);
}
