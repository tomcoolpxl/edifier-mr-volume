namespace EdifierMrVolume.Core;

/// <summary>One known speaker profile: what it advertises and which GATT service holds the controls.</summary>
public sealed record SpeakerModel(string Name, Guid AdvertisedUuid, Guid ServiceUuid);

/// <summary>A decoded packet from the speaker.</summary>
public sealed record Frame(byte Header, byte AppCode, byte Command, byte[] Payload, bool ChecksumOk);

/// <summary>Hardware volume as reported by the speaker.</summary>
public readonly record struct VolumeLevel(int Current, int Max);

/// <summary>
/// The Edifier ConneX BLE control protocol, as far as this app needs it.
/// Frame: AA appCode command lenHi lenLo payload... checksum, where checksum = sum of all
/// previous bytes &amp; 0xFF. Replies start with BB (or CC/DD) instead of AA.
/// Verified on an MR4.5 on 2026-11-15; see README.md.
/// </summary>
public static class EdifierProtocol
{
    public const ushort ManufacturerId = 0x07E0;
    public const string AdvertisedName = "EDIFIER BLE";

    public static readonly Guid NotifyCharacteristic = new("48090001-1a48-11e9-ab14-d663bd873d93");
    public static readonly Guid WriteCharacteristic = new("48090002-1a48-11e9-ab14-d663bd873d93");

    public static readonly IReadOnlyList<SpeakerModel> Models =
    [
        // Verified on hardware with this app.
        new("MR4.5", new("00006303-0000-1000-8000-00805f9b34fb"), new("48096303-1a48-11e9-ab14-d663bd873d93")),
        // Later MR4.5 revision with the 8625 DSP (UUIDs from EdifierControl; not tested here).
        new("MR4.5 (8625)", new("00007c03-0000-1000-8000-00805f9b34fb"), new("48097c03-1a48-11e9-ab14-d663bd873d93")),
        // MR3: same frames and [max, current] volume reply, own service (verified on an MR3 by
        // omarchy-edifier-mr3 and EdifierControl; not tested with this app).
        new("MR3", new("0000f300-0000-1000-8000-00805f9b34fb"), new("4809f301-1a48-11e9-ab14-d663bd873d93")),
        // MR5: byte-identical volume frames per MR5 captures in Edifier-IR-remote and
        // omarchy-edifier-mr5; not tested with this app.
        new("MR5", new("00003a01-0000-1000-8000-00805f9b34fb"), new("48093a01-1a48-11e9-ab14-d663bd873d93")),
        // Later MR5 revision with the 8625 DSP (UUIDs from EdifierControl; not tested here).
        new("MR5 (8625)", new("00007e03-0000-1000-8000-00805f9b34fb"), new("48097e03-1a48-11e9-ab14-d663bd873d93")),
    ];

    public static SpeakerModel? ModelForService(Guid serviceUuid) =>
        Models.FirstOrDefault(m => m.ServiceUuid == serviceUuid);

    /// <summary>Client identifier byte. The speaker answers any value; it replies with 0xEC.</summary>
    public const byte AppCode = 0x0C;

    public const byte CmdGetVolume = 0x66;
    public const byte CmdSetVolume = 0x67;
    /// <summary>Firmware version; the MR4.5 answers <c>01 00 00</c> for 1.0.0.</summary>
    public const byte CmdGetFirmware = 0xC6;
    /// <summary>Model name as ASCII; the MR4.5 answers "EDIFIER MR4.5".</summary>
    public const byte CmdGetModelName = 0xC9;
    /// <summary>Sound mode and room tuning: [mode][index][byte0][lowCut][slope][space][desktop].</summary>
    public const byte CmdGetSoundMode = 0xD5;
    /// <summary>Sets the same 7 bytes; the speaker acks 01 and pushes 0xD5.</summary>
    public const byte CmdSetSoundMode = 0xC4;
    /// <summary>Switches the speakers off; they must be switched on by hand.</summary>
    public const byte CmdPowerOff = 0xCE;

    public static readonly IReadOnlyList<string> SoundModeNames = ["Monitor", "Music", "Custom"];

    /// <summary>The sound-mode reply: the mode plus the six room-tuning bytes, kept as reported.</summary>
    public sealed record SoundState(int Mode, byte[] Tuning)
    {
        public string Name => Mode >= 0 && Mode < SoundModeNames.Count ? SoundModeNames[Mode] : $"Mode {Mode}";
        public bool Equals(SoundState? other) =>
            other is not null && Mode == other.Mode && Tuning.AsSpan().SequenceEqual(other.Tuning);
        public override int GetHashCode() => HashCode.Combine(Mode, Tuning.Length > 0 ? Tuning[0] : 0);
    }

    public static bool TryDecodeSoundMode(Frame frame, out SoundState state)
    {
        state = null!;
        // Exactly 7 bytes: anything else and we must not echo it back.
        if (!frame.ChecksumOk || frame.Command != CmdGetSoundMode || frame.Payload.Length != 7) return false;
        state = new SoundState(frame.Payload[0], frame.Payload[1..]);
        return true;
    }

    /// <summary>
    /// Switches the sound mode, echoing the speaker's own six tuning bytes unchanged. Building the
    /// payload any other way changed the room tuning in a test (see TODO.md), so this refuses
    /// anything but exactly the reported bytes.
    /// </summary>
    public static byte[] SetSoundMode(SoundState current, int mode)
    {
        if (current.Tuning.Length != 6) throw new ArgumentException("Need the 6 tuning bytes the speaker reported.", nameof(current));
        if (mode < 0 || mode >= SoundModeNames.Count) throw new ArgumentOutOfRangeException(nameof(mode));
        return BuildCommand(CmdSetSoundMode, [(byte)mode, .. current.Tuning]);
    }

    /// <summary>
    /// Room tuning (ConneX "Acoustic Tuning"), the last four of the six bytes after the mode:
    /// low cut in Hz (20-100, 5 Hz steps), its slope (0-3 = 6/12/18/24 dB per octave), acoustic
    /// space (0-4 = 0 to −4 dB, middle of the room to a corner) and desktop control (0/1). From
    /// the MR4.5 manual (section 8), omarchy-edifier-mr5 and EdifierControl; see README.md.
    /// </summary>
    public sealed record RoomTuning(int LowCutHz, int Slope, int Space, bool Desktop)
    {
        public const int MinLowCutHz = 20;
        public const int MaxLowCutHz = 100;
        public const int LowCutStepHz = 5;
        public const int MaxSlope = 3;
        public const int MaxSpace = 4;

        /// <summary>6, 12, 18 or 24.</summary>
        public int SlopeDbPerOctave => 6 * (Slope + 1);

        public bool IsValid =>
            LowCutHz is >= MinLowCutHz and <= MaxLowCutHz && LowCutHz % LowCutStepHz == 0
            && Slope is >= 0 and <= MaxSlope && Space is >= 0 and <= MaxSpace;
    }

    /// <summary>
    /// Reads the room tuning from a sound-mode reply. False for anything outside the known
    /// values, or with the DSP-reset byte set, so such bytes are never taken as a base to write.
    /// </summary>
    public static bool TryDecodeRoomTuning(SoundState sound, out RoomTuning tuning)
    {
        tuning = null!;
        var t = sound.Tuning;
        if (t.Length != 6 || t[1] != 0 || t[5] > 1) return false;
        var decoded = new RoomTuning(t[2], t[3], t[4], t[5] == 1);
        if (!decoded.IsValid) return false;
        tuning = decoded;
        return true;
    }

    /// <summary>
    /// Sets the room tuning: the speaker's own mode, index and DSP byte are echoed, and only the
    /// four tuning values are replaced, each checked against its range. Refuses when the speaker's
    /// reply isn't understood (see TryDecodeRoomTuning) or a value is out of range.
    /// </summary>
    public static byte[] SetRoomTuning(SoundState current, RoomTuning tuning)
    {
        if (!TryDecodeRoomTuning(current, out _))
            throw new ArgumentException("The speaker's tuning bytes aren't understood; not writing.", nameof(current));
        if (current.Mode < 0 || current.Mode >= SoundModeNames.Count) throw new ArgumentOutOfRangeException(nameof(current));
        if (!tuning.IsValid) throw new ArgumentOutOfRangeException(nameof(tuning));
        return BuildCommand(CmdSetSoundMode,
        [
            (byte)current.Mode, current.Tuning[0], current.Tuning[1],
            (byte)tuning.LowCutHz, (byte)tuning.Slope, (byte)tuning.Space, (byte)(tuning.Desktop ? 1 : 0),
        ]);
    }

    public static byte[] PowerOff() => BuildCommand(CmdPowerOff);

    /// <summary>
    /// Power saving (standby after 15 minutes without sound): [00 off | 01 on]. Also pushed by
    /// the speaker when it is switched with the knob (volume 0, double-press). Found on an MR4.5,
    /// 2026-11-16.
    /// </summary>
    public const byte CmdGetPowerSaving = 0xB1;
    /// <summary>Sets power saving: [00 | 01]; the speaker answers with the previous value.</summary>
    public const byte CmdSetPowerSaving = 0xB2;

    public static bool TryDecodePowerSaving(Frame frame, out bool on)
    {
        on = false;
        if (!frame.ChecksumOk || frame.Command != CmdGetPowerSaving || frame.Payload.Length != 1 || frame.Payload[0] > 1)
            return false;
        on = frame.Payload[0] == 1;
        return true;
    }

    public static byte[] SetPowerSaving(bool on) => BuildCommand(CmdSetPowerSaving, [(byte)(on ? 1 : 0)]);

    /// <summary>
    /// Custom EQ curve (heard in Custom mode): [layout][bandCount][slot], then per band
    /// [index][freqHi][freqLo][gain], then a 4-byte stamp and the curve's name. Layout 12 is the
    /// 9-band MR4.5/MR5 curve, 7 the 6-band one (ConneX's eqIndex, per EdifierControl and
    /// omarchy-edifier-mr5); gain 0-12 with 6 flat. How many dB a step is isn't published
    /// (EdifierControl shows 0.5 dB, omarchy-edifier-mr3 assumes 1 dB), so the app shows steps.
    /// </summary>
    public const byte CmdGetEq = 0x43;
    /// <summary>Sets one band: [slot][index][freqHi][freqLo][gain]; the speaker acks 01.</summary>
    public const byte CmdSetEqBand = 0x44;

    public sealed record EqBand(int Index, int Frequency, int Gain);

    /// <summary>The custom EQ curve as the speaker reported it.</summary>
    public sealed record EqCurve(int Layout, byte Slot, IReadOnlyList<EqBand> Bands)
    {
        /// <summary>Highest gain byte; the MR3 clamps anything above it.</summary>
        public const int MaxGain = 12;

        /// <summary>The gain byte for "no change".</summary>
        public int Flat => MaxGain / 2;

        /// <summary>Largest step up or down from flat.</summary>
        public int Range => MaxGain - Flat;

        /// <summary>Each band as steps from flat (−6 to +6 on the MR4.5).</summary>
        public int[] Steps => Bands.Select(b => b.Gain - Flat).ToArray();

        public EqCurve WithGain(int band, int gain) =>
            this with { Bands = Bands.Select((b, i) => i == band ? b with { Gain = gain } : b).ToArray() };

        public bool Equals(EqCurve? other) =>
            other is not null && Layout == other.Layout && Slot == other.Slot && Bands.SequenceEqual(other.Bands);

        public override int GetHashCode() => HashCode.Combine(Layout, Slot, Bands.Count);
    }

    public static bool TryDecodeEq(Frame frame, out EqCurve curve)
    {
        curve = null!;
        var p = frame.Payload;
        if (!frame.ChecksumOk || frame.Command != CmdGetEq || p.Length < 3) return false;
        int layout = p[0], count = p[1];
        // Only the layouts with a slot byte and 4-byte bands; others are laid out differently.
        if (layout is not (7 or 12) || count is < 1 or > 16 || p.Length < 3 + 4 * count) return false;
        var bands = new EqBand[count];
        for (var i = 0; i < count; i++)
        {
            var at = 3 + 4 * i;
            bands[i] = new EqBand(p[at], (p[at + 1] << 8) | p[at + 2], p[at + 3]);
            if (bands[i].Gain > EqCurve.MaxGain) return false;
        }
        curve = new EqCurve(layout, p[2], bands);
        return true;
    }

    /// <summary>
    /// Sets one band, by its position in the reported curve, to a gain byte (0-12). The
    /// slot, band index and frequency are echoed from the speaker's own reply.
    /// </summary>
    public static byte[] SetEqBand(EqCurve current, int band, int gain)
    {
        var b = current.Bands[band];
        gain = Math.Clamp(gain, 0, EqCurve.MaxGain);
        return BuildCommand(CmdSetEqBand,
            [current.Slot, (byte)b.Index, (byte)(b.Frequency >> 8), (byte)(b.Frequency & 0xFF), (byte)gain]);
    }

    /// <summary>Volume range the MR4.5 reports when it doesn't say otherwise.</summary>
    public const int DefaultMaxVolume = 30;

    public static byte[] BuildCommand(byte command, ReadOnlySpan<byte> payload = default)
    {
        if (payload.Length > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(payload));
        var packet = new byte[5 + payload.Length + 1];
        packet[0] = 0xAA;
        packet[1] = AppCode;
        packet[2] = command;
        packet[3] = (byte)(payload.Length >> 8);
        packet[4] = (byte)payload.Length;
        payload.CopyTo(packet.AsSpan(5));
        packet[^1] = Checksum(packet.AsSpan(0, packet.Length - 1));
        return packet;
    }

    public static byte[] GetVolume() => BuildCommand(CmdGetVolume);

    public static byte[] SetVolume(int level, int max = DefaultMaxVolume)
    {
        var clamped = (byte)Math.Clamp(level, 0, max);
        return BuildCommand(CmdSetVolume, [clamped]);
    }

    /// <summary>Splits one BLE notification into frames. Stops at the first malformed byte.</summary>
    public static IReadOnlyList<Frame> ParseFrames(ReadOnlySpan<byte> data)
    {
        var frames = new List<Frame>();
        while (data.Length >= 6 && IsHeader(data[0]))
        {
            var payloadLength = (data[3] << 8) | data[4];
            var total = 5 + payloadLength + 1;
            if (data.Length < total) break;
            var frame = data[..total];
            frames.Add(new Frame(
                frame[0], frame[1], frame[2],
                frame.Slice(5, payloadLength).ToArray(),
                Checksum(frame[..^1]) == frame[^1]));
            data = data[total..];
        }
        return frames;
    }

    /// <summary>
    /// Decodes a get-volume reply (also pushed by the speaker after a set). The MR4.5 sends
    /// [max, current]; single-byte replies (other models) are [current].
    /// </summary>
    public static bool TryDecodeVolume(Frame frame, out VolumeLevel level)
    {
        level = default;
        if (!frame.ChecksumOk || frame.Command != CmdGetVolume || frame.Payload.Length == 0) return false;
        level = frame.Payload.Length >= 2
            ? new VolumeLevel(frame.Payload[1], frame.Payload[0])
            : new VolumeLevel(frame.Payload[0], DefaultMaxVolume);
        return level.Max > 0 && level.Current <= level.Max;
    }

    public static bool TryDecodeFirmware(Frame frame, out string version)
    {
        version = "";
        if (!frame.ChecksumOk || frame.Command != CmdGetFirmware || frame.Payload.Length == 0) return false;
        version = string.Join('.', frame.Payload);
        return true;
    }

    public static bool TryDecodeModelName(Frame frame, out string name)
    {
        name = "";
        if (!frame.ChecksumOk || frame.Command != CmdGetModelName || frame.Payload.Length == 0) return false;
        name = System.Text.Encoding.UTF8.GetString(frame.Payload).Trim('\0', ' ');
        return name.Length > 0;
    }

    public static SpeakerModel? ModelForAdvertisedUuid(Guid uuid) =>
        Models.FirstOrDefault(m => m.AdvertisedUuid == uuid);

    /// <summary>
    /// True if an advertisement looks like an Edifier monitor's control interface: a known
    /// model UUID, or the "EDIFIER BLE" name with Edifier's manufacturer id (how the MR3 is
    /// usually found). The exact model is settled after connecting, by its GATT service.
    /// </summary>
    public static bool IsControlAdvertisement(IEnumerable<Guid> serviceUuids, string? localName, IEnumerable<ushort> companyIds) =>
        serviceUuids.Any(u => ModelForAdvertisedUuid(u) is not null)
        || (string.Equals(localName, AdvertisedName, StringComparison.Ordinal) && companyIds.Contains(ManufacturerId));

    public static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    private static bool IsHeader(byte b) => b is 0xAA or 0xBB or 0xCC or 0xDD;

    private static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        var sum = 0;
        foreach (var b in bytes) sum += b;
        return (byte)sum;
    }
}
