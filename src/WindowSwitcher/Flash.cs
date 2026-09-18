using System.Diagnostics;
using WindowSwitcher.Native;
using static WindowSwitcher.Native.Win32;

namespace WindowSwitcher;

/// <summary>
/// Marks the window you switched to, three times in 300 ms: either a border along the inside of its
/// visible frame (<see cref="EdgeFrame"/>, so it also shows on a maximized window), which blinks on and
/// off in 50 ms phases, or a translucent fill over the whole frame (<see cref="FillWindow"/>), which
/// fades up and down instead of blinking. Both let clicks through. A thread-pool thread times the phases
/// with a high-resolution waitable timer (a USER timer ticks in ~15.6 ms steps) and toggles the windows
/// with ShowWindowAsync. The fill window holds an image buffer of the window's size only during its
/// flash; afterwards it is shrunk to 1x1.
/// </summary>
internal static unsafe class Flash
{
    const int Phases = 6;
    const int PhaseMs = 50;
    const byte FillAlpha = 90; // ~35%

    // The fill's fade: three pulses of 100 ms, each rising to FillAlpha, falling back to nothing, then a
    // gap with the window hidden, so the pulses stay separate rather than running into one another.
    const int Pulses = 3;
    const int PulseMs = 100;
    const int RiseMs = 30;
    const int FallEndMs = 70;
    const int StepMs = 5;

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
            if (mode == SwitchFlash.Window) Fade(generation, timer, start);
            else Blink(generation, timer, start);
        }
        finally
        {
            if (timer != 0) Kernel32.CloseHandle(timer);
        }
    }

    /// <summary>The border: on and off in equal phases.</summary>
    static void Blink(int generation, nint timer, long start)
    {
        for (int phase = 0; phase < Phases; phase++)
        {
            if (generation != Volatile.Read(ref s_generation)) return; // a newer flash owns the windows
            s_frame!.ShowAsync(phase % 2 == 0);
            if (phase < Phases - 1)
                WaitUntil(timer, start + (phase + 1) * PhaseMs * Stopwatch.Frequency / 1000);
        }
    }

    /// <summary>
    /// The fill: each pulse fades up to <see cref="FillAlpha"/> and back to nothing by stepping the
    /// window's layered alpha, then hides it for the rest of the pulse. Alpha is a window attribute
    /// rather than a message, so it can be set from this thread.
    /// </summary>
    static void Fade(int generation, nint timer, long start)
    {
        for (int pulse = 0; pulse < Pulses; pulse++)
        {
            int pulseStart = pulse * PulseMs;
            for (int t = StepMs; t <= FallEndMs; t += StepMs)
            {
                if (generation != Volatile.Read(ref s_generation)) return;
                s_fill!.SetAlpha(AlphaAt(t));
                if (t == StepMs) s_fill.ShowAsync(true); // set the first alpha before it is ever seen
                WaitUntil(timer, start + (pulseStart + t) * Stopwatch.Frequency / 1000);
            }
            if (generation != Volatile.Read(ref s_generation)) return;
            s_fill!.ShowAsync(false);
            if (pulse < Pulses - 1)
                WaitUntil(timer, start + (pulseStart + PulseMs) * Stopwatch.Frequency / 1000);
        }
        if (generation == Volatile.Read(ref s_generation))
            s_fill!.ReleaseAsync();
    }

    /// <summary>The pulse's shape: up to full by <see cref="RiseMs"/>, back to nothing by <see cref="FallEndMs"/>.</summary>
    static byte AlphaAt(int t) =>
        (byte)(t <= RiseMs ? FillAlpha * t / RiseMs : FillAlpha * (FallEndMs - t) / (FallEndMs - RiseMs));

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
