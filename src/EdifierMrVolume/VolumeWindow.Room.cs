using EdifierMrVolume.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace EdifierMrVolume;

/// <summary>The room-tuning settings.</summary>
public sealed partial class VolumeWindow
{
    private bool _updatingRoom;
    /// <summary>Tuning this app asked for and the speaker hasn't reported yet; shown meanwhile.</summary>
    private EdifierProtocol.RoomTuning? _roomTarget;
    private long _roomTargetUntil;

    /// <summary>Shows the speaker's room tuning; disabled (and says why) until it is known.</summary>
    private void UpdateRoom(EdifierProtocol.SoundState? sound)
    {
        EdifierProtocol.RoomTuning? tuning = null;
        if (sound is not null && EdifierProtocol.TryDecodeRoomTuning(sound, out var decoded)) tuning = decoded;
        var usable = tuning is not null;
        WallCard.IsEnabled = usable;
        BassCard.IsEnabled = usable;
        DeskCard.IsEnabled = usable;
        RoomExpander.Description = usable ? "Bass for where the speakers stand."
            : sound is null ? NotConnectedYet
            : "The speaker reports a room setting this app doesn't know. Change it in the Edifier app.";
        if (tuning is null)
        {
            _roomTarget = null;
            return;
        }
        if (_roomTarget is not null)
        {
            if (tuning != _roomTarget && Environment.TickCount64 < _roomTargetUntil) return;
            _roomTarget = null;
        }
        ShowRoom(tuning);
    }

    private void ShowRoom(EdifierProtocol.RoomTuning tuning)
    {
        _updatingRoom = true;
        try
        {
            if (WallSlider.Value != tuning.Space) WallSlider.Value = tuning.Space;
            WallNumbers.Text = RoomPresets.SpaceNumbers(tuning.Space);
            var bass = RoomPresets.MatchBass(tuning);
            var index = bass is null ? -1 : RoomPresets.Bass.ToList().IndexOf(bass);
            if (BassBox.SelectedIndex != index) BassBox.SelectedIndex = index;
            BassCard.Description = RoomPresets.BassNumbers(tuning);
            if (DeskSwitch.IsOn != tuning.Desktop) DeskSwitch.IsOn = tuning.Desktop;
        }
        finally
        {
            _updatingRoom = false;
        }
    }

    private void WallSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) => ApplyRoom();

    private void BassBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyRoom();

    private void DeskSwitch_Toggled(object sender, RoutedEventArgs e) => ApplyRoom();

    /// <summary>One of the three room controls moved: send all four values together.</summary>
    private void ApplyRoom()
    {
        if (_updatingRoom || BassBox is null || DeskSwitch is null || _speaker.SoundMode is not { } sound
            || !EdifierProtocol.TryDecodeRoomTuning(sound, out var current)) return;
        var shown = _roomTarget ?? current;
        var index = BassBox.SelectedIndex;
        var bass = index >= 0 && index < RoomPresets.Bass.Count ? RoomPresets.Bass[index] : null;
        var wanted = new EdifierProtocol.RoomTuning(
            bass?.LowCutHz ?? shown.LowCutHz,
            bass?.Slope ?? shown.Slope,
            (int)Math.Round(WallSlider.Value),
            DeskSwitch.IsOn);
        if (wanted == shown || !_speaker.RequestRoomTuning(wanted)) return;
        _roomTarget = wanted;
        _roomTargetUntil = Environment.TickCount64 + 5_000;
        ShowRoom(wanted);
        Log.Write($"Room -> {bass?.Name ?? "bass as set"}, space {wanted.Space}, desk {(wanted.Desktop ? "on" : "off")}");
    }
}
