using System.Diagnostics;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// Marks the window you switched to, three times in 300 ms (50 ms on, 50 ms off): either a border along
/// the inside of its visible frame (<see cref="EdgeFrame"/>, so it also shows on a maximized window) or a
/// translucent fill over the whole frame (<see cref="FillWindow"/>). Both let clicks through. A
/// thread-pool thread times the phases with a high-resolution waitable timer (a USER timer ticks in
/// ~15.6 ms steps) and toggles the windows with ShowWindowAsync. The fill window holds an image buffer
/// of the window's size only during its flash; afterwards it is shrunk to 1x1.
/// </summary>
internal static unsafe class Flash
{
    const int Phases = 6;
    const int PhaseMs = 50;
    const byte FillAlpha = 90; // ~35%

    static EdgeFrame? s_frame;
    static FillWindow? s_fill;
    static int s_generation;

    public static void Init(nint instance)
    {
        s_frame = new EdgeFrame(instance, "WindowSwitcher.Flash");
        s_fill = new FillWindow(instance, "WindowSwitcher.FlashFill", FillAlpha);
    }

    /// <summary>Starts the flash around a window; a flash still running is replaced.</summary>
    public static void Start(nint hwnd, uint color, SwitchFlash mode)
    {
        if (s_frame is null || s_fill is null) return;
        int generation = Interlocked.Increment(ref s_generation);
        s_frame.Show(false);
        s_fill.Show(false);
        if (mode == SwitchFlash.None) return;

        RECT r;
        if (Dwm.DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &r, sizeof(RECT)) < 0)
            User32.GetWindowRect(hwnd, out r);
        if (r.Width <= 0 || r.Height <= 0) return;

        if (mode == SwitchFlash.Window)
        {
            s_fill.SetColor(color);
            s_fill.Place(r, HWND_TOPMOST);
        }
        else
        {
            uint dpi = User32.GetDpiForWindow(hwnd);
            int thickness = Math.Max(2, (int)Math.Round(4 * (dpi == 0 ? 96 : dpi) / 96.0));
            s_frame.SetColor(color);
            s_frame.Place(r, thickness, HWND_TOPMOST);
        }
        _ = Task.Run(() => Run(generation, mode));
    }

    static void Run(int generation, SwitchFlash mode)
    {
        nint timer = Kernel32.CreateWaitableTimerExW(0, null, Kernel32.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Kernel32.TIMER_ALL_ACCESS);
        try
        {
            long start = Stopwatch.GetTimestamp();
            for (int phase = 0; phase < Phases; phase++)
            {
                if (generation != Volatile.Read(ref s_generation)) return; // a newer flash owns the windows
                bool on = phase % 2 == 0;
                if (mode == SwitchFlash.Window) s_fill!.ShowAsync(on);
                else s_frame!.ShowAsync(on);
                if (phase < Phases - 1)
                    WaitUntil(timer, start + (phase + 1) * PhaseMs * Stopwatch.Frequency / 1000);
            }
            if (mode == SwitchFlash.Window && generation == Volatile.Read(ref s_generation))
                s_fill!.ReleaseAsync();
        }
        finally
        {
            if (timer != 0) Kernel32.CloseHandle(timer);
        }
    }

    static void WaitUntil(nint timer, long deadline)
    {
        long remaining = deadline - Stopwatch.GetTimestamp();
        if (remaining <= 0) return;
        long due = -(remaining * 10_000_000 / Stopwatch.Frequency); // negative: relative, in 100 ns
        if (timer != 0 && Kernel32.SetWaitableTimer(timer, &due, 0, 0, 0, false))
            Kernel32.WaitForSingleObject(timer, Kernel32.INFINITE);
        else
            Thread.Sleep((int)(remaining * 1000 / Stopwatch.Frequency));
    }
}
