namespace EdifierMrVolume.Core;

/// <summary>
/// The next sound-mode command (0xC4) to send for a pending room tuning and a pending mode.
/// Both share one command that carries the mode and all six tuning bytes, so the packet is
/// built from the speaker's latest reported bytes at the moment it is sent, never when it was
/// asked for: a mode switch and a tuning change made close together can't undo each other.
/// Only the latest request of each kind counts.
/// </summary>
public static class SoundWrites
{
    /// <summary>
    /// The packet to send now, or null when neither request changes anything. A pending tuning
    /// goes first (<paramref name="forTuning"/> is true); the mode follows once the speaker has
    /// taken the tuning and reported or acked it.
    /// </summary>
    public static byte[]? Next(EdifierProtocol.SoundState? current, int? mode, EdifierProtocol.RoomTuning? tuning,
        out bool forTuning)
    {
        forTuning = false;
        if (current is null) return null;
        if (tuning is not null && tuning.IsValid
            && EdifierProtocol.TryDecodeRoomTuning(current, out var now) && now != tuning)
        {
            forTuning = true;
            return EdifierProtocol.SetRoomTuning(current, tuning);
        }
        if (mode is { } m && m != current.Mode && m >= 0 && m < EdifierProtocol.SoundModeNames.Count
            && current.Tuning.Length == 6)
        {
            return EdifierProtocol.SetSoundMode(current, m);
        }
        return null;
    }
}
