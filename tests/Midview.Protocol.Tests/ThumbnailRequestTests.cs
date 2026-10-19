using Midview.Protocol;
using Xunit;

public class ThumbnailRequestTests
{
    [Theory]
    [InlineData(640, 360)]
    [InlineData(1, 1)]
    [InlineData(ThumbnailRequest.MaxWidth, ThumbnailRequest.MaxHeight)]
    public void EncodeThenDecode_RoundTrips(int width, int height)
    {
        Assert.Equal((width, height), ThumbnailRequest.Decode(ThumbnailRequest.Encode(width, height)));
    }

    [Fact]
    public void AnEmptyRequest_FromAnOlderMaster_GetsTheDefaultSize()
    {
        Assert.Equal(
            (ThumbnailRequest.DefaultWidth, ThumbnailRequest.DefaultHeight),
            ThumbnailRequest.Decode(Array.Empty<byte>()));
    }

    [Theory]
    [InlineData(new byte[] { 1, 64, 0 })]
    [InlineData(new byte[] { 1, 64, 0, 180, 0 })]
    [InlineData(new byte[] { 0, 0, 0, 180 })] // no width
    [InlineData(new byte[] { 1, 64, 0, 0 })] // no height
    public void AnythingElse_GetsTheDefaultSize(byte[] payload)
    {
        Assert.Equal((ThumbnailRequest.DefaultWidth, ThumbnailRequest.DefaultHeight), ThumbnailRequest.Decode(payload));
    }

    [Fact]
    public void ASizePastTheMaximum_IsCappedToIt()
    {
        // 2000 x 2000: no Master sends it, but the Session must not render it.
        var payload = new byte[] { 0x07, 0xD0, 0x07, 0xD0 };

        Assert.Equal((ThumbnailRequest.MaxWidth, ThumbnailRequest.MaxHeight), ThumbnailRequest.Decode(payload));
    }

    [Theory]
    [InlineData(0, 180)]
    [InlineData(320, 0)]
    [InlineData(ThumbnailRequest.MaxWidth + 1, 180)]
    [InlineData(320, ThumbnailRequest.MaxHeight + 1)]
    public void Encode_RefusesASizeOutsideTheLimits(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ThumbnailRequest.Encode(width, height));
    }
}
