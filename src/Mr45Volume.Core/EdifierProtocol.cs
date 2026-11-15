namespace Mr45Volume.Core;

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
    ];

    /// <summary>Client identifier byte. The speaker answers any value; it replies with 0xEC.</summary>
    public const byte AppCode = 0x0C;

    public const byte CmdGetVolume = 0x66;
    public const byte CmdSetVolume = 0x67;

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
