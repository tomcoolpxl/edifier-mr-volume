using EdifierMrVolume.Core;

namespace EdifierMrVolume.Core.Tests;

/// <summary>Byte sequences captured from a real MR4.5 on 2026-11-15.</summary>
public class EdifierProtocolTests
{
    private static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", ""));

    [Fact]
    public void GetVolume_matches_captured_packet() =>
        Assert.Equal(Bytes("AA 0C 66 00 00 1C"), EdifierProtocol.GetVolume());

    [Theory]
    [InlineData(15, "AA 0C 67 00 01 0F 2D")]
    [InlineData(16, "AA 0C 67 00 01 10 2E")]
    public void SetVolume_matches_captured_packet(int level, string expected) =>
        Assert.Equal(Bytes(expected), EdifierProtocol.SetVolume(level));

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(31, 30)]
    [InlineData(200, 30)]
    public void SetVolume_clamps_to_range(int requested, byte sent) =>
        Assert.Equal(sent, EdifierProtocol.SetVolume(requested)[5]);

    [Theory]
    [InlineData("BB EC 66 00 02 1E 13 40", 19)]
    [InlineData("BB EC 66 00 02 1E 10 3D", 16)]
    [InlineData("BB EC 66 00 02 1E 0F 3C", 15)]
    public void Volume_reply_decodes_as_max_then_current(string reply, int current)
    {
        var frame = Assert.Single(EdifierProtocol.ParseFrames(Bytes(reply)));
        Assert.True(frame.ChecksumOk);
        Assert.True(EdifierProtocol.TryDecodeVolume(frame, out var level));
        Assert.Equal(new VolumeLevel(current, 30), level);
    }

    [Fact]
    public void Set_ack_is_not_a_volume()
    {
        var frame = Assert.Single(EdifierProtocol.ParseFrames(Bytes("BB EC 67 00 01 01 10")));
        Assert.True(frame.ChecksumOk);
        Assert.Equal(EdifierProtocol.CmdSetVolume, frame.Command);
        Assert.False(EdifierProtocol.TryDecodeVolume(frame, out _));
    }

    [Fact]
    public void Bad_checksum_is_rejected()
    {
        var frame = Assert.Single(EdifierProtocol.ParseFrames(Bytes("BB EC 66 00 02 1E 13 41")));
        Assert.False(frame.ChecksumOk);
        Assert.False(EdifierProtocol.TryDecodeVolume(frame, out _));
    }

    [Fact]
    public void Two_frames_in_one_notification_are_split()
    {
        var frames = EdifierProtocol.ParseFrames(Bytes("BB EC 67 00 01 01 10 BB EC 66 00 02 1E 0F 3C"));
        Assert.Equal([EdifierProtocol.CmdSetVolume, EdifierProtocol.CmdGetVolume], frames.Select(f => f.Command));
    }

    [Theory]
    [InlineData("")]
    [InlineData("00 01 02 03 04 05 06")]
    [InlineData("BB EC 66 00 05 1E")]
    public void Garbage_and_truncated_data_yield_no_frames(string hex) =>
        Assert.Empty(EdifierProtocol.ParseFrames(Bytes(hex)));

    [Theory]
    [InlineData("00006303-0000-1000-8000-00805f9b34fb", "MR4.5")]
    [InlineData("0000f300-0000-1000-8000-00805f9b34fb", "MR3")]
    [InlineData("00003a01-0000-1000-8000-00805f9b34fb", "MR5")]
    public void Advertised_uuid_maps_to_model(string uuid, string model) =>
        Assert.Equal(model, EdifierProtocol.ModelForAdvertisedUuid(new Guid(uuid))?.Name);

    [Theory]
    [InlineData("48096303-1a48-11e9-ab14-d663bd873d93", "MR4.5")]
    [InlineData("4809f301-1a48-11e9-ab14-d663bd873d93", "MR3")]
    [InlineData("48093a01-1a48-11e9-ab14-d663bd873d93", "MR5")]
    [InlineData("48097e03-1a48-11e9-ab14-d663bd873d93", "MR5 (8625)")]
    public void Control_service_maps_to_model(string uuid, string model) =>
        Assert.Equal(model, EdifierProtocol.ModelForService(new Guid(uuid))?.Name);

    [Fact]
    public void Generic_services_are_not_a_model() =>
        Assert.Null(EdifierProtocol.ModelForService(new Guid("00001800-0000-1000-8000-00805f9b34fb")));

    [Fact]
    public void Sound_mode_reply_decodes()
    {
        var frame = Assert.Single(EdifierProtocol.ParseFrames(Bytes("BB EC D5 00 07 00 02 00 1E 03 00 01 A7")));
        Assert.True(EdifierProtocol.TryDecodeSoundMode(frame, out var state));
        Assert.Equal(0, state.Mode);
        Assert.Equal("Monitor", state.Name);
        Assert.Equal(Bytes("02 00 1E 03 00 01"), state.Tuning);
    }

    /// <summary>The exact packet that switched the MR4.5 to Music in the 2026-11-16 test.</summary>
    [Fact]
    public void Switching_to_music_echoes_the_tuning_bytes()
    {
        var frame = Assert.Single(EdifierProtocol.ParseFrames(Bytes("BB EC D5 00 07 00 02 00 1E 03 00 01 A7")));
        Assert.True(EdifierProtocol.TryDecodeSoundMode(frame, out var state));
        Assert.Equal(Bytes("AA 0C C4 00 07 01 02 00 1E 03 00 01 A6"), EdifierProtocol.SetSoundMode(state, 1));
    }

    /// <summary>A short payload once changed the room tuning; it must never be echoed.</summary>
    [Fact]
    public void Short_or_long_sound_mode_replies_are_rejected()
    {
        var shortFrame = Assert.Single(EdifierProtocol.ParseFrames(Bytes("BB EC D5 00 06 00 02 1E 03 00 01 A5")));
        Assert.False(EdifierProtocol.TryDecodeSoundMode(shortFrame, out _));
        Assert.Throws<ArgumentException>(() => EdifierProtocol.SetSoundMode(new EdifierProtocol.SoundState(0, [2, 0, 0x1E]), 1));
    }

    [Fact]
    public void Power_off_packet() => Assert.Equal(Bytes("AA 0C CE 00 00 84"), EdifierProtocol.PowerOff());

    [Fact]
    public void Firmware_reply_decodes()
    {
        var frame = Assert.Single(EdifierProtocol.ParseFrames(Bytes("BB EC C6 00 03 01 00 00 71")));
        Assert.True(EdifierProtocol.TryDecodeFirmware(frame, out var version));
        Assert.Equal("1.0.0", version);
    }

    [Fact]
    public void Model_name_reply_decodes()
    {
        var frame = Assert.Single(EdifierProtocol.ParseFrames(
            Bytes("BB EC C9 00 0D 45 44 49 46 49 45 52 20 4D 52 34 2E 35 CB")));
        Assert.True(EdifierProtocol.TryDecodeModelName(frame, out var name));
        Assert.Equal("EDIFIER MR4.5", name);
    }

    [Fact]
    public void Volume_reply_is_not_firmware_or_name()
    {
        var frame = Assert.Single(EdifierProtocol.ParseFrames(Bytes("BB EC 66 00 02 1E 13 40")));
        Assert.False(EdifierProtocol.TryDecodeFirmware(frame, out _));
        Assert.False(EdifierProtocol.TryDecodeModelName(frame, out _));
    }

    /// <summary>MR5 capture published by Edifier-IR-remote (PROTOCOL.md): same layout as the MR4.5.</summary>
    [Fact]
    public void Mr5_volume_capture_decodes()
    {
        var frame = Assert.Single(EdifierProtocol.ParseFrames(Bytes("BB EC 66 00 02 1E 08 35")));
        Assert.True(EdifierProtocol.TryDecodeVolume(frame, out var level));
        Assert.Equal(new VolumeLevel(8, 30), level);
    }

    [Fact]
    public void Mr3_uses_its_own_control_service() =>
        Assert.Equal(new Guid("4809f301-1a48-11e9-ab14-d663bd873d93"),
            EdifierProtocol.Models.Single(m => m.Name == "MR3").ServiceUuid);

    [Fact]
    public void Advertisement_matches_on_known_uuid() =>
        Assert.True(EdifierProtocol.IsControlAdvertisement(
            [new Guid("00006303-0000-1000-8000-00805f9b34fb")], null, []));

    [Fact]
    public void Advertisement_matches_on_name_plus_edifier_manufacturer() =>
        Assert.True(EdifierProtocol.IsControlAdvertisement([], "EDIFIER BLE", [0x07E0]));

    [Theory]
    [InlineData("EDIFIER BLE", (ushort)0x004C)]
    [InlineData("EDIFIER MR3", (ushort)0x07E0)]
    [InlineData(null, (ushort)0x07E0)]
    public void Other_advertisements_are_ignored(string? name, ushort companyId) =>
        Assert.False(EdifierProtocol.IsControlAdvertisement([Guid.NewGuid()], name, [companyId]));
}
