using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using WindowSwitcher.Native;

namespace WindowSwitcher;

/// <summary>
/// <c>WindowSwitcher.exe --dump &lt;dir&gt;</c>: lists the windows on the monitor under the pointer, grouped
/// as the popup groups them, captures each non-minimized one, and writes <c>dump.txt</c> plus one PNG per
/// thumbnail. Used by the phase gates.
/// </summary>
internal static class Dump
{
    const int BoxWidth = 480;
    const int BoxHeight = 270;
    const int TimeoutMs = 1000;

    public static int Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var report = new StringBuilder();
        try
        {
            RunCore(dir, report);
            File.WriteAllText(Path.Combine(dir, "dump.txt"), report.ToString());
            return 0;
        }
        catch (Exception e)
        {
            report.AppendLine().AppendLine($"FAILED: {e}");
            File.WriteAllText(Path.Combine(dir, "dump.txt"), report.ToString());
            return 1;
        }
    }

    static void RunCore(string dir, StringBuilder report)
    {
        User32.GetCursorPos(out POINT pt);
        nint monitor = User32.MonitorFromPoint(pt, Win32.MONITOR_DEFAULTTONEAREST);
        var (bounds, work, dpi) = Monitors.Describe(monitor);
        report.AppendLine($"pointer {pt.X},{pt.Y}  monitor {bounds}  work {work}  dpi {dpi}");
        report.AppendLine($"capture supported: {CaptureService.IsSupported}");

        long t0 = Stopwatch.GetTimestamp();
        var groups = WindowList.ForMonitor(monitor);
        report.AppendLine($"enumeration: {Stopwatch.GetElapsedTime(t0).TotalMilliseconds:F1} ms, {groups.Count} programs, {groups.Sum(g => g.Windows.Count)} windows");

        t0 = Stopwatch.GetTimestamp();
        // WinRT capture objects are created on a thread-pool (MTA) thread, as the app does.
        using var capture = Task.Run(CaptureService.CreateAsync).GetAwaiter().GetResult();
        report.AppendLine($"device + access: {Stopwatch.GetElapsedTime(t0).TotalMilliseconds:F1} ms");

        var jobs = new List<(string Name, WindowInfo Window, Task<(CapturedImage? Image, string? Error, double Ms)> Task)>();
        long all = Stopwatch.GetTimestamp();
        for (int g = 0; g < groups.Count; g++)
        {
            for (int w = 0; w < groups[g].Windows.Count; w++)
            {
                var window = groups[g].Windows[w];
                if (window.Minimized) continue;
                jobs.Add(($"g{g + 1}-w{w + 1}", window, Task.Run(async () =>
                {
                    long start = Stopwatch.GetTimestamp();
                    try
                    {
                        var image = await capture.CaptureAsync(window.Hwnd, BoxWidth, BoxHeight, TimeoutMs);
                        return ((CapturedImage?)image, (string?)null, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    }
                    catch (Exception e)
                    {
                        return (null, $"{e.GetType().Name}: {e.Message}", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    }
                })));
            }
        }
        Task.WaitAll(jobs.Select(j => (Task)j.Task).ToArray());
        report.AppendLine($"all captures: {Stopwatch.GetElapsedTime(all).TotalMilliseconds:F1} ms for {jobs.Count} windows");
        report.AppendLine();

        var results = jobs.ToDictionary(j => j.Window.Hwnd, j => (j.Name, j.Task.Result));
        for (int g = 0; g < groups.Count; g++)
        {
            report.AppendLine($"[{g + 1}] {groups[g].Key}");
            for (int w = 0; w < groups[g].Windows.Count; w++)
            {
                var window = groups[g].Windows[w];
                string head = $"    g{g + 1}-w{w + 1}  hwnd {window.Hwnd:X}  \"{window.Title}\"  {window.Bounds.Width}x{window.Bounds.Height}";
                if (window.Minimized)
                {
                    report.AppendLine($"{head}  minimized: DWM image in the popup, not captured");
                    continue;
                }
                var (name, (image, error, ms)) = results[window.Hwnd];
                if (image is null)
                {
                    report.AppendLine($"{head}  capture FAILED after {ms:F0} ms: {error}");
                    continue;
                }
                string file = name + ".png";
                Png.Write(Path.Combine(dir, file), image);
                report.AppendLine($"{head}  capture {ms:F0} ms  thumb {image.Width}x{image.Height}  " +
                    $"alpha0 {AlphaZeroPercent(image):F1}%  hash {Hash(image)}  -> {file}");
            }
        }
    }

    static double AlphaZeroPercent(CapturedImage image)
    {
        int zero = 0;
        for (int i = 3; i < image.Pixels.Length; i += 4)
            if (image.Pixels[i] == 0) zero++;
        return 100.0 * zero / (image.Width * image.Height);
    }

    static string Hash(CapturedImage image) => Convert.ToHexString(SHA256.HashData(image.Pixels))[..16];
}
