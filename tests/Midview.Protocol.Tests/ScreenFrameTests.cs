using Midview.Protocol;
using Xunit;

public class ScreenFrameTests
{
    [Fact]
    public void EncodeThenDecode_GivesTheSameFrame()
    {
        var frame = new ScreenFrame(7, 1920, 1080, new[]
        {
            new ScreenRegion(0, 0, 1920, 64, new byte[] { 1, 2, 3 }),
            new ScreenRegion(128, 1024, 64, 56, new byte[] { 4, 5 }),
        });

        var decoded = ScreenFrame.Decode(frame.Encode());

        Assert.NotNull(decoded);
        Assert.Equal((7, 1920, 1080), (decoded.FrameId, decoded.ScreenWidth, decoded.ScreenHeight));
        Assert.Equal(2, decoded.Regions.Count);
        Assert.Equal((0, 0, 1920, 64), (decoded.Regions[0].X, decoded.Regions[0].Y, decoded.Regions[0].Width, decoded.Regions[0].Height));
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Regions[0].Jpeg);
        Assert.Equal((128, 1024, 64, 56), (decoded.Regions[1].X, decoded.Regions[1].Y, decoded.Regions[1].Width, decoded.Regions[1].Height));
        Assert.Equal(new byte[] { 4, 5 }, decoded.Regions[1].Jpeg);
    }

    [Fact]
    public void Decode_NoRegions_IsAFrameWithoutRegions()
    {
        var decoded = ScreenFrame.Decode(new ScreenFrame(1, 800, 600, Array.Empty<ScreenRegion>()).Encode());

        Assert.NotNull(decoded);
        Assert.Empty(decoded.Regions);
    }

    public static TheoryData<string, byte[]> Malformed()
    {
        var good = new ScreenFrame(1, 800, 600, new[] { new ScreenRegion(0, 0, 64, 64, new byte[] { 9, 9 }) }).Encode();
        return new TheoryData<string, byte[]>
        {
            { "empty", Array.Empty<byte>() },
            { "cut short", good[..^1] },
            { "extra bytes", good.Concat(new byte[] { 0 }).ToArray() },
            { "screen too big", new ScreenFrame(1, ScreenFrame.MaxScreenSide + 1, 600, Array.Empty<ScreenRegion>()).Encode() },
            { "screen of too many pixels", new ScreenFrame(1, ScreenFrame.MaxScreenSide, ScreenFrame.MaxScreenSide, Array.Empty<ScreenRegion>()).Encode() },
            { "no screen", new ScreenFrame(1, 0, 600, Array.Empty<ScreenRegion>()).Encode() },
            { "region past the right edge", new ScreenFrame(1, 800, 600, new[] { new ScreenRegion(780, 0, 64, 64, new byte[] { 1 }) }).Encode() },
            { "region past the bottom", new ScreenFrame(1, 800, 600, new[] { new ScreenRegion(0, 590, 64, 64, new byte[] { 1 }) }).Encode() },
            { "region left of the screen", new ScreenFrame(1, 800, 600, new[] { new ScreenRegion(-1, 0, 64, 64, new byte[] { 1 }) }).Encode() },
            { "empty region", new ScreenFrame(1, 800, 600, new[] { new ScreenRegion(0, 0, 0, 64, new byte[] { 1 }) }).Encode() },
            { "huge region count", WithInt(good, 12, int.MaxValue) },
            { "far more area than the screen", new ScreenFrame(1, 800, 600, Enumerable.Repeat(new ScreenRegion(0, 0, 800, 600, new byte[] { 1 }), 3).ToArray()).Encode() },
            { "JPEG longer than the message", WithInt(good, 32, 1000) },
            { "negative JPEG length", WithInt(good, 32, -1) },
        };
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Decode_Malformed_IsNull(string what, byte[] payload)
    {
        Assert.True(ScreenFrame.Decode(payload) is null, what);
    }

    // The most a Session sends: the area around its changes, never more than
    // twice the screen's (edge tiles counted whole).
    [Fact]
    public void Decode_TwiceTheScreensArea_IsAccepted()
    {
        var regions = Enumerable.Repeat(new ScreenRegion(0, 0, 800, 600, new byte[] { 1 }), 2).ToArray();

        Assert.NotNull(ScreenFrame.Decode(new ScreenFrame(1, 800, 600, regions).Encode()));
    }

    [Fact]
    public void Decode_An8KScreen_IsAccepted()
    {
        Assert.NotNull(ScreenFrame.Decode(new ScreenFrame(1, 7680, 4320, Array.Empty<ScreenRegion>()).Encode()));
    }

    [Fact]
    public void Split_ASmallUpdate_IsOneMessage()
    {
        var regions = new[] { Region(1000), Region(2000) };

        var groups = ScreenFrame.Split(regions);

        Assert.Equal(new[] { regions }, groups);
    }

    [Fact]
    public void Split_AHugeUpdate_KeepsEachMessageUnderTheLimit()
    {
        var big = ScreenFrame.MaxJpegBytesPerMessage / 2 + 1;
        var regions = new[]
        {
            Region(big), Region(1000), Region(big), Region(big), Region(ScreenFrame.MaxJpegBytesPerMessage + 5),
        };

        var groups = ScreenFrame.Split(regions);

        Assert.Equal(new[] { 2, 1, 1, 1 }, groups.Select(group => group.Count));
        Assert.Equal(regions, groups.SelectMany(group => group));
        Assert.All(groups.Where(group => group.Count > 1),
            group => Assert.True(group.Sum(region => region.Jpeg.Length) <= ScreenFrame.MaxJpegBytesPerMessage));
    }

    [Fact]
    public void Split_NoRegions_IsNoMessages()
    {
        Assert.Empty(ScreenFrame.Split(Array.Empty<ScreenRegion>()));
    }

    [Fact]
    public void Ack_RoundTrips()
    {
        Assert.Equal(123456, ScreenFrame.DecodeAck(ScreenFrame.EncodeAck(123456)));
        Assert.Null(ScreenFrame.DecodeAck(new byte[] { 1, 2 }));
    }

    private static ScreenRegion Region(int jpegBytes) => new(0, 0, 64, 64, new byte[jpegBytes]);

    private static byte[] WithInt(byte[] payload, int offset, int value)
    {
        var copy = payload.ToArray();
        BitConverter.GetBytes(value).CopyTo(copy, offset);
        return copy;
    }
}
