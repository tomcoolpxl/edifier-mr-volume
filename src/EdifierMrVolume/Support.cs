using System.Diagnostics;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;

namespace EdifierMrVolume;

internal sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}

/// <summary>User preferences, kept in %LOCALAPPDATA%\EdifierMrVolume\settings.json.</summary>
internal sealed class AppSettings
{
    public static readonly int[] HideChoices = [1, 2, 3, 5];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string FilePath => Path.Combine(Log.Folder, "settings.json");

    /// <summary>Pop the flyout up (without taking focus) when the volume is changed on the speaker.</summary>
    public bool ShowOnKnobTurn { get; set; }

    /// <summary>How long that pop-up stays after the last change.</summary>
    public int HideAfterSeconds { get; set; } = HideChoices[0];

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) is { } loaded)
            {
                if (!HideChoices.Contains(loaded.HideAfterSeconds)) loaded.HideAfterSeconds = HideChoices[0];
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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Write($"Could not save settings: {ex.Message}");
        }
    }
}

/// <summary>Per-user "start with Windows" via the HKCU Run key.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EdifierMrVolume";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string command
                && Environment.ProcessPath is { } exe
                && command.Contains(exe, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
        {
            key.SetValue(ValueName, $"\"{exe}\"");
            Log.Write($"Autostart on: {exe}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Write("Autostart off.");
        }
    }
}

/// <summary>Small rolling log in %LOCALAPPDATA%\EdifierMrVolume.</summary>
internal static class Log
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    public static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdifierMrVolume");

    public static readonly string FilePath = Path.Combine(Folder, "log.txt");

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        Debug.WriteLine(line);
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(Folder, "log.old.txt"), overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
