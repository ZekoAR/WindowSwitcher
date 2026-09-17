using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowSwitcher;

internal sealed class HotkeySettings
{
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }

    [JsonIgnore]
    public bool IsEmpty => !Ctrl && !Shift && !Alt && !Win;

    public static HotkeySettings DefaultThisMonitor() => new() { Ctrl = true, Shift = true };

    public static HotkeySettings DefaultAllMonitors() => new() { Ctrl = true, Shift = true, Alt = true };

    public bool SameKeys(HotkeySettings other) =>
        Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt && Win == other.Win;

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

/// <summary>What marks the window you switched to.</summary>
internal enum SwitchFlash
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>A border along the window's edge flashes three times.</summary>
    Border,

    /// <summary>The whole window lights up three times.</summary>
    Window,
}

/// <summary>How the popup's rows (programs) and the windows in them are ordered.</summary>
internal enum RowOrder
{
    /// <summary>In the order each program and window was first seen; switching never reshuffles them.</summary>
    Fixed,

    /// <summary>Left to right by where the windows are on the screen: rows by their leftmost window.</summary>
    Horizontal,
}

/// <summary>User settings, stored as WindowSwitcher.json next to the exe.</summary>
internal sealed class Settings
{
    public const int DefaultThumbnailHeight = 105; // 75% of the first design's 140 (owner, 2026-09-17)
    public const int MinThumbnailHeight = 60;
    public const int MaxThumbnailHeight = 400;
    public const int DefaultDimPercent = 30;
    public const int MaxDimPercent = 80;

    /// <summary>The keys that show the windows on the pointer's monitor.</summary>
    public HotkeySettings Hotkey { get; set; } = HotkeySettings.DefaultThisMonitor();

    /// <summary>The keys that show the windows on every monitor.</summary>
    public HotkeySettings AllMonitorsHotkey { get; set; } = HotkeySettings.DefaultAllMonitors();

    /// <summary>Thumbnail height in pixels at 100% display scaling.</summary>
    public int ThumbnailHeight { get; set; } = DefaultThumbnailHeight;

    /// <summary>How much darker everything but the popup and the hovered window gets, 0 (off) to 80.</summary>
    public int DimPercent { get; set; } = DefaultDimPercent;

    public SwitchFlash SwitchFlash { get; set; } = SwitchFlash.Border;

    public RowOrder RowOrder { get; set; } = RowOrder.Fixed;

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
        if (settings.Hotkey is null || settings.Hotkey.IsEmpty)
            settings.Hotkey = HotkeySettings.DefaultThisMonitor();
        if (settings.AllMonitorsHotkey is null || settings.AllMonitorsHotkey.IsEmpty)
            settings.AllMonitorsHotkey = HotkeySettings.DefaultAllMonitors();
        settings.ThumbnailHeight = Math.Clamp(settings.ThumbnailHeight, MinThumbnailHeight, MaxThumbnailHeight);
        settings.DimPercent = Math.Clamp(settings.DimPercent, 0, MaxDimPercent);
        if (!Enum.IsDefined(settings.SwitchFlash)) settings.SwitchFlash = SwitchFlash.Border;
        if (!Enum.IsDefined(settings.RowOrder)) settings.RowOrder = RowOrder.Fixed;
        return settings;
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.Settings));
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
