using Midview.Protocol;
using Xunit;

public class PhoneWallpaperTests
{
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9 };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7 };

    [Fact]
    public void TheMessageAndTheScreens_AreTheWireNumbers()
    {
        Assert.Equal(105, (int)MessageType.SetPhoneWallpaper);
        Assert.Equal(1, (int)PhoneWallpaperScreens.Home);
        Assert.Equal(2, (int)PhoneWallpaperScreens.Lock);
        Assert.Equal(3, (int)PhoneWallpaperScreens.Both);
    }

    [Theory]
    [InlineData(PhoneWallpaperScreens.Home)]
    [InlineData(PhoneWallpaperScreens.Lock)]
    [InlineData(PhoneWallpaperScreens.Both)]
    public void AJpegOrAPng_RoundTripsOnEachScreen(PhoneWallpaperScreens screens)
    {
        foreach (var image in new[] { Jpeg, Png })
        {
            var payload = PhoneWallpaper.Encode(screens, image);
            Assert.Equal((byte)screens, payload[0]);
            var decoded = PhoneWallpaper.Decode(payload);
            Assert.NotNull(decoded);
            Assert.Equal(screens, decoded!.Value.Screens);
            Assert.Equal(image, decoded.Value.Image);
        }
    }

    [Fact]
    public void NoPicture_PutsThePhonesOwnBack()
    {
        var payload = PhoneWallpaper.Encode(PhoneWallpaperScreens.Both, Array.Empty<byte>());
        Assert.Equal(new byte[] { 3 }, payload);
        var decoded = PhoneWallpaper.Decode(payload);
        Assert.NotNull(decoded);
        Assert.Empty(decoded!.Value.Image);
    }

    [Fact]
    public void TheBiggestPicture_StillFits()
    {
        var image = new byte[PhoneWallpaper.MaxImageBytes];
        Jpeg.CopyTo(image, 0);
        var payload = PhoneWallpaper.Encode(PhoneWallpaperScreens.Home, image);
        Assert.Equal(image.Length, PhoneWallpaper.Decode(payload)!.Value.Image.Length);
        Assert.True(payload.Length < MessageFramer.MaxMessageLength);
    }

    [Fact]
    public void Encode_RefusesWrongScreensTooBigOrNotAnImage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PhoneWallpaper.Encode(0, Jpeg));
        Assert.Throws<ArgumentOutOfRangeException>(() => PhoneWallpaper.Encode((PhoneWallpaperScreens)4, Jpeg));
        var tooBig = new byte[PhoneWallpaper.MaxImageBytes + 1];
        Jpeg.CopyTo(tooBig, 0);
        Assert.Throws<ArgumentException>(() => PhoneWallpaper.Encode(PhoneWallpaperScreens.Both, tooBig));
        Assert.Throws<ArgumentException>(() => PhoneWallpaper.Encode(PhoneWallpaperScreens.Both, "GIF89a"u8.ToArray()));
    }

    [Fact]
    public void Decode_RefusesAnyOtherPayload()
    {
        Assert.Null(PhoneWallpaper.Decode(Array.Empty<byte>()));
        Assert.Null(PhoneWallpaper.Decode(new byte[] { 0 }));
        Assert.Null(PhoneWallpaper.Decode(new byte[] { 4 }));
        Assert.Null(PhoneWallpaper.Decode(new byte[] { 0xFF, 0xFF, 0xD8, 0xFF }));
        Assert.Null(PhoneWallpaper.Decode(new byte[] { 1, (byte)'G', (byte)'I', (byte)'F' }));
        Assert.Null(PhoneWallpaper.Decode(new byte[] { 3, 0xFF, 0xD8 }));
        var tooBig = new byte[1 + PhoneWallpaper.MaxImageBytes + 1];
        tooBig[0] = 1;
        Jpeg.CopyTo(tooBig, 1);
        Assert.Null(PhoneWallpaper.Decode(tooBig));
    }
}
