using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowSwitcher;

internal sealed class HotkeySettings
{
    public bool Ctrl { get; set; } = true;
    public bool Shift { get; set; } = true;
    public bool Alt { get; set; }
    public bool Win { get; set; }

    [JsonIgnore]
    public bool IsEmpty => !Ctrl && !Shift && !Alt && !Win;

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Win) parts.Add("Win");
        return string.Join("+", parts);
    }
}

/// <summary>User settings, stored as WindowSwitcher.json next to the exe.</summary>
internal sealed class Settings
{
    public const int DefaultThumbnailHeight = 140;
    public const int MinThumbnailHeight = 60;
    public const int MaxThumbnailHeight = 400;

    public HotkeySettings Hotkey { get; set; } = new();

    /// <summary>Thumbnail height in pixels at 100% display scaling.</summary>
    public int ThumbnailHeight { get; set; } = DefaultThumbnailHeight;

    public static string FilePath =>
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "WindowSwitcher.json");

    /// <summary>Loads the settings file; a missing file gives defaults, an unreadable one gives defaults and an error.</summary>
    public static Settings Load(out string? error)
    {
        error = null;
        Settings? settings = null;
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.Settings);
        }
        catch (Exception e)
        {
            error = $"{FilePath} could not be read, so the defaults are used.\n\n{e.Message}";
        }
        settings ??= new Settings();
        settings.Hotkey ??= new HotkeySettings();
        if (settings.Hotkey.IsEmpty) settings.Hotkey = new HotkeySettings();
        settings.ThumbnailHeight = Math.Clamp(settings.ThumbnailHeight, MinThumbnailHeight, MaxThumbnailHeight);
        return settings;
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.Settings));
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
