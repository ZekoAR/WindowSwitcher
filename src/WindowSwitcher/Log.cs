namespace WindowSwitcher;

/// <summary>Problems worth knowing about, appended to WindowSwitcher.log next to the exe (capped at 1 MB).</summary>
internal static class Log
{
    static readonly Lock Gate = new();

    static string FilePath =>
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "WindowSwitcher.log");

    public static void Error(string what, Exception? e = null) =>
        Write(e is null ? what : $"{what}: {e.GetType().Name}: {e.Message}");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1024 * 1024) info.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down (read-only folder, file in use).
        }
    }
}
