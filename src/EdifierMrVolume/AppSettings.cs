using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdifierMrVolume;

/// <summary>User preferences, kept in %LOCALAPPDATA%\EdifierMrVolume\settings.json.</summary>
internal sealed class AppSettings
{
    public static readonly int[] HideChoices = [1, 2, 3, 5];

    private static string FilePath => Path.Combine(Log.Folder, "settings.json");

    /// <summary>Pop the flyout up (without taking focus) when the volume is changed on the speaker.</summary>
    public bool ShowOnKnobTurn { get; set; }

    /// <summary>How long that pop-up stays after the last change.</summary>
    public int HideAfterSeconds { get; set; } = HideChoices[0];

    /// <summary>
    /// Cap what the app sets (slider, wheel, start volume). The knob isn't limited. On for new
    /// installs, at 24, since studio monitors at 30 are very loud; a saved
    /// setting is kept.
    /// </summary>
    public bool VolumeLimitEnabled { get; set; } = true;
    public int VolumeLimit { get; set; } = 24;

    /// <summary>Set this level the first time the app connects after it starts (not on reconnects).</summary>
    public bool StartVolumeEnabled { get; set; }
    public int StartVolume { get; set; } = 7;

    /// <summary>Keyboard shortcuts in any app (modifiers + Up, Down, M). Off by default.</summary>
    public bool HotkeysEnabled { get; set; }
    public string HotkeyModifiers { get; set; } = Hotkeys.ModifierChoices[0];

    /// <summary>Let MCP apps (Claude, VS Code) control the speakers through "--mcp". Off by default.</summary>
    public bool McpEnabled { get; set; }

    /// <summary>The one-time hint about Windows hiding new notification-area icons was shown.</summary>
    public bool FirstRunDone { get; set; }

    /// <summary>Turn the speakers off when Windows goes to sleep. Off by default.</summary>
    public bool PowerOffOnSleep { get; set; }

    /// <summary>Equalizer choice: a preset name or "Custom"; null until the speaker's curve is known.</summary>
    public string? EqChoice { get; set; }

    /// <summary>
    /// The user's own curve, as steps from flat per band (lowest band first). The speaker has one
    /// custom curve, which a preset overwrites, so this is what "Custom" puts back.
    /// </summary>
    public int[]? CustomEq { get; set; }

    /// <summary>The limit to pass to the connection, or null when off. Not saved.</summary>
    [JsonIgnore]
    public int? EffectiveVolumeLimit => VolumeLimitEnabled ? VolumeLimit : null;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.AppSettings) is { } loaded)
            {
                if (!HideChoices.Contains(loaded.HideAfterSeconds)) loaded.HideAfterSeconds = HideChoices[0];
                loaded.VolumeLimit = Math.Clamp(loaded.VolumeLimit, 1, 30);
                loaded.StartVolume = Math.Clamp(loaded.StartVolume, 0, 30);
                if (!Hotkeys.ModifierChoices.Contains(loaded.HotkeyModifiers)) loaded.HotkeyModifiers = Hotkeys.ModifierChoices[0];
                return loaded;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Settings unreadable, using defaults: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.Folder);
            // Written next to it, then swapped in: an interrupted write (power loss, a killed
            // process) leaves the old file whole instead of a truncated one that loads as defaults.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Write($"Could not save settings: {ex.Message}");
        }
    }
}

/// <summary>
/// settings.json without reflection: generated at build time, as Native AOT needs (same file
/// format as before).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
