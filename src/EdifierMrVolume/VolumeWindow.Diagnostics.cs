using System.Diagnostics;
using System.Globalization;
using EdifierMrVolume.Core;
using Microsoft.UI.Xaml;

namespace EdifierMrVolume;

/// <summary>Diagnostics and About.</summary>
public sealed partial class VolumeWindow
{
    public void UpdateDiagnostics(SpeakerDiagnostics d, string displayModel, string appVersion)
    {
        DiagStatus.Text = d.State switch
        {
            SpeakerState.Connected when d.ConnectedSince is { } since =>
                $"Connected since {since.ToString("T", CultureInfo.CurrentCulture)}",
            SpeakerState.Connected => "Connected",
            SpeakerState.Connecting => "Connecting",
            SpeakerState.Searching => "Looking for the speaker",
            _ => "Released",
        };
        DiagExpander.Description = DiagStatus.Text;
        // Mention the name the speaker reports only when it says something the model doesn't.
        DiagSpeaker.Text = d.DeviceName is { } name && d.Model is { } model
                           && !name.Contains(model, StringComparison.OrdinalIgnoreCase)
            ? $"{displayModel} (reports \"{name}\")"
            : displayModel;
        DiagFirmware.Text = d.Firmware ?? "–";
        DiagAddress.Text = d.Address ?? "–";
        DiagReconnects.Text = d.Reconnects.ToString();
        DiagError.Text = d.LastError ?? "None";
        AboutExpander.Description = $"Version {appVersion}";
    }

    /// <summary>
    /// Reconnect (Diagnostics and the connection bar). It stays enabled, so keyboard focus
    /// doesn't drop while it works; a second press meanwhile does nothing.
    /// </summary>
    private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_reconnecting) return;
        _reconnecting = true;
        try
        {
            await _commands.Reconnect();
        }
        finally
        {
            _reconnecting = false;
        }
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e) => Open(Log.FilePath);

    private void GitHubLink_Click(object sender, RoutedEventArgs e) => Open(RepoUrl);

    private void ReportLink_Click(object sender, RoutedEventArgs e) =>
        Open($"{RepoUrl}/issues/new?template=bug_report.yml");

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"Could not open {target}: {ex.Message}");
        }
    }
}
