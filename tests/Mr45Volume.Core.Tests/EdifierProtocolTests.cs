using Mr45Volume.Core;

namespace Mr45Volume.Core.Tests;

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

    [Fact]
    public void Advertised_uuid_maps_to_mr45() =>
        Assert.Equal("MR4.5", EdifierProtocol.ModelForAdvertisedUuid(new Guid("00006303-0000-1000-8000-00805f9b34fb"))?.Name);
}
