using System.Diagnostics;

namespace EdifierMrVolume;

/// <summary>
/// Keeps the app's settings and log in the user's real %LOCALAPPDATA%\EdifierMrVolume.
///
/// A program started by a packaged (MSIX) desktop app can run inside that app's container,
/// where Windows silently redirects its writes to %LOCALAPPDATA% into the packaged app's private
/// folder (%LOCALAPPDATA%\Packages\&lt;app&gt;\LocalCache\Local). The app then works, but its
/// settings and log exist only there: "Open log" makes Notepad report "The system cannot find
/// the path specified", and the copy that starts at sign-in starts from scratch. The process
/// carries no package identity, so the only reliable test is to write a marker and see where
/// it lands. A program started through Explorer runs outside any container (tested
/// 2026-11-16), so a contained copy starts itself again that way and quits.
/// </summary>
internal static class ContainerEscape
{
    private const string FolderName = "EdifierMrVolume";
    private static readonly TimeSpan RelaunchGuard = TimeSpan.FromSeconds(30);

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string PackagesFolder => Path.Combine(LocalAppData, "Packages");

    /// <summary>
    /// True when this copy should quit because it has started itself again outside a container.
    /// </summary>
    public static bool RelaunchIfContained()
    {
        var redirected = FindRedirectedDataFolder();
        if (redirected is null) return false;

        // Never loop: if a relaunch from this container happened moments ago, run here anyway.
        var guard = Path.Combine(redirected, "relaunched.txt");
        try
        {
            if (File.Exists(guard) && DateTime.UtcNow - File.GetLastWriteTimeUtc(guard) < RelaunchGuard)
            {
                Log.Write($"Running inside another app's container ({redirected}); relaunching didn't help.");
                return false;
            }
            File.WriteAllText(guard, DateTime.UtcNow.ToString("O"));
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Environment.ProcessPath}\"") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"Running inside another app's container; could not start again outside it: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The container folder this process's %LOCALAPPDATA%\EdifierMrVolume writes go to, or null
    /// when they go to the real folder.
    /// </summary>
    private static string? FindRedirectedDataFolder()
    {
        if (!Directory.Exists(PackagesFolder)) return null;
        var markerName = $".where-am-i-{Guid.NewGuid():N}";
        var marker = Path.Combine(Log.Folder, markerName);
        try
        {
            Directory.CreateDirectory(Log.Folder);
            File.WriteAllText(marker, "");
            foreach (var candidate in ContainerDataFolders())
            {
                if (File.Exists(Path.Combine(candidate, markerName))) return candidate;
            }
            return null;
        }
        catch
        {
            return null; // can't tell; carry on as usual
        }
        finally
        {
            try { File.Delete(marker); } catch { }
        }
    }

    /// <summary>Every container copy of the app's data folder.</summary>
    private static IEnumerable<string> ContainerDataFolders()
    {
        foreach (var package in Directory.EnumerateDirectories(PackagesFolder))
        {
            var folder = Path.Combine(package, "LocalCache", "Local", FolderName);
            if (Directory.Exists(folder)) yield return folder;
        }
    }

    /// <summary>
    /// Outside a container: if the real data folder has no settings yet but a container copy
    /// does (a copy that ran inside one), take its settings and log over, and remove the copy.
    /// </summary>
    public static void ImportStrandedData()
    {
        try
        {
            var settings = Path.Combine(Log.Folder, "settings.json");
            if (File.Exists(settings) || !Directory.Exists(PackagesFolder)) return;
            var source = ContainerDataFolders()
                .Where(f => File.Exists(Path.Combine(f, "settings.json")))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(Path.Combine(f, "settings.json")))
                .FirstOrDefault();
            if (source is null) return;
            Directory.CreateDirectory(Log.Folder);
            foreach (var name in new[] { "settings.json", "log.old.txt", "log.txt" })
            {
                var from = Path.Combine(source, name);
                if (File.Exists(from)) File.Copy(from, Path.Combine(Log.Folder, name), overwrite: true);
            }
            Log.Write($"Took over the settings and log from {source}, where a copy started inside another app's container had kept them.");
            try { Directory.Delete(source, recursive: true); } catch { }
        }
        catch (Exception ex)
        {
            Log.Write($"Could not take over settings from a container copy: {ex.Message}");
        }
    }
}
