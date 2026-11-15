using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Win32;

namespace EdifierMrVolume;

internal sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
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
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdifierMrVolume");
    private static readonly string FilePath = Path.Combine(Folder, "log.txt");

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
