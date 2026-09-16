using WindowSwitcher.Native;

namespace WindowSwitcher;

internal static unsafe class Monitors
{
    /// <summary>A monitor's bounds, work area (taskbar excluded) and effective DPI, all in physical pixels.</summary>
    public static (RECT Bounds, RECT Work, uint Dpi) Describe(nint monitor)
    {
        MONITORINFO mi = new() { cbSize = (uint)sizeof(MONITORINFO) };
        User32.GetMonitorInfoW(monitor, &mi);
        if (Shell32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out uint dpi, out _) < 0)
            dpi = 96;
        return (mi.rcMonitor, mi.rcWork, dpi);
    }
}
