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

    // A real baseline JPEG, 2x1 px. The same bytes are in the Kotlin test
    // (ScreenFrameTest.kt in the Android agent); keep them identical.
    private const string JpegHex =
        "ffd8ffe000104a46494600010100000100010000ffdb004300100b0c0e0c0a100e0d0e1211101318281a181616183123251d" +
        "283a333d3c3933383740485c4e404457453738506d51575f626768673e4d71797064785c656763ffdb004301111212181518" +
        "2f1a1a2f63423842636363636363636363636363636363636363636363636363636363636363636363636363636363636363" +
        "6363636363636363ffc00011080001000203012200021101031101ffc4001f00000105010101010101000000000000000001" +
        "02030405060708090a0bffc400b5100002010303020403050504040000017d01020300041105122131410613516107227114" +
        "328191a1082342b1c11552d1f02433627282090a161718191a25262728292a3435363738393a434445464748494a53545556" +
        "5758595a636465666768696a737475767778797a838485868788898a92939495969798999aa2a3a4a5a6a7a8a9aab2b3b4b5" +
        "b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae1e2e3e4e5e6e7e8e9eaf1f2f3f4f5f6f7f8f9faffc4001f010003" +
        "0101010101010101010000000000000102030405060708090a0bffc400b51100020102040403040705040400010277000102" +
        "031104052131061241510761711322328108144291a1b1c109233352f0156272d10a162434e125f11718191a262728292a35" +
        "363738393a434445464748494a535455565758595a636465666768696a737475767778797a82838485868788898a92939495" +
        "969798999aa2a3a4a5a6a7a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae2e3e4e5e6e7e8e9ea" +
        "f2f3f4f5f6f7f8f9faffda000c03010002110311003f00e426ff005cff00ef1fe7451457dbd0fe147d118d6fe24bd59fffd9";

    // What the Kotlin agent's ScreenFrame.encode writes for frame 1, a 2x1
    // screen and one region covering it.
    [Fact]
    public void Kotlin_frame_vector_decodes()
    {
        var jpeg = Convert.FromHexString(JpegHex);
        var payload = new byte[]
        {
            1, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0,
            (byte)(jpeg.Length & 0xFF), (byte)(jpeg.Length >> 8), 0, 0,
        }.Concat(jpeg).ToArray();

        var frame = ScreenFrame.Decode(payload);

        Assert.NotNull(frame);
        Assert.Equal(1, frame!.FrameId);
        Assert.Equal(2, frame.ScreenWidth);
        Assert.Equal(1, frame.ScreenHeight);
        var region = Assert.Single(frame.Regions);
        Assert.Equal((0, 0, 2, 1), (region.X, region.Y, region.Width, region.Height));
        Assert.Equal(jpeg, region.Jpeg);
        Assert.Equal(payload, frame.Encode());
    }
}
