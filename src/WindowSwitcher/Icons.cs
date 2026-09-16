using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// Program icons. Icons a window reports belong to that window and are never destroyed here; icons
/// extracted from an executable are cached for the app's lifetime.
/// </summary>
internal static unsafe class Icons
{
    const uint GetIconTimeoutMs = 50;
    const int AppIconResource = 32512; // the compiler's resource id for <ApplicationIcon>
    const int IDI_APPLICATION = 32512;

    static readonly Dictionary<string, nint> ExeIcons = new(StringComparer.OrdinalIgnoreCase);
    static nint s_defaultIcon;

    /// <summary>A small icon for a tile header: the window's own icon first.</summary>
    public static nint ForHeader(nint hwnd, string? exe, int size)
    {
        nint icon = WindowIcon(hwnd);
        if (icon == 0) icon = ExeIcon(exe, size);
        return icon != 0 ? icon : Default();
    }

    /// <summary>A large icon for a placeholder: the executable's icon at the exact size first.</summary>
    public static nint ForPlaceholder(nint hwnd, string? exe, int size)
    {
        nint icon = ExeIcon(exe, size);
        if (icon == 0) icon = WindowIcon(hwnd);
        return icon != 0 ? icon : Default();
    }

    /// <summary>WindowSwitcher's own icon at a system metric size (LIM_SMALL, LIM_LARGE).</summary>
    public static nint AppIcon(int metric) =>
        User32.LoadIconMetric(Kernel32.GetModuleHandleW(null), AppIconResource, metric, out nint icon) >= 0 ? icon : Default();

    static nint WindowIcon(nint hwnd)
    {
        User32.SendMessageTimeoutW(hwnd, WM_GETICON, ICON_BIG, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, GetIconTimeoutMs, out nint icon);
        if (icon == 0)
            User32.SendMessageTimeoutW(hwnd, WM_GETICON, ICON_SMALL2, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, GetIconTimeoutMs, out icon);
        if (icon == 0)
            icon = (nint)User32.GetClassLongPtrW(hwnd, GCLP_HICON);
        return icon;
    }

    static nint ExeIcon(string? exe, int size)
    {
        if (exe is null) return 0;
        string key = $"{size}|{exe}";
        if (ExeIcons.TryGetValue(key, out nint cached)) return cached;
        nint icon = 0;
        uint id = 0;
        if (User32.PrivateExtractIconsW(exe, 0, size, size, &icon, &id, 1, 0) != 1 || icon == -1)
            icon = 0;
        ExeIcons[key] = icon;
        return icon;
    }

    static nint Default()
    {
        if (s_defaultIcon == 0)
            s_defaultIcon = User32.LoadImageW(0, IDI_APPLICATION, IMAGE_ICON, 0, 0, LR_SHARED);
        return s_defaultIcon;
    }
}
