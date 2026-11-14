using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// The teacher's picture made phone-sized: kept in shape, never enlarged,
// turned upright, a JPEG.
public class PhoneWallpaperImageTests
{
    [Theory]
    [InlineData(1080, 2400, 1080, 2400)]
    [InlineData(1440, 3200, 1440, 3200)]
    [InlineData(3000, 4000, 1440, 1920)]
    [InlineData(4000, 3000, 1920, 1440)]
    [InlineData(1500, 4000, 1200, 3200)]
    [InlineData(6000, 1000, 3200, 533)]
    [InlineData(640, 480, 640, 480)]
    public void It_fits_a_phones_biggest_screen_keeping_its_shape(int width, int height, int fitWidth, int fitHeight) =>
        Assert.Equal((fitWidth, fitHeight), PhoneWallpaperImage.FitSize(width, height));

    [Fact]
    public void A_picture_with_no_size_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PhoneWallpaperImage.FitSize(0, 10));

    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(2, 0, true)]
    [InlineData(3, 180, false)]
    [InlineData(4, 180, true)]
    [InlineData(5, 90, true)]
    [InlineData(6, 90, false)]
    [InlineData(7, 270, true)]
    [InlineData(8, 270, false)]
    [InlineData(0, 0, false)]
    [InlineData(9, 0, false)]
    public void Exif_orientation_turns_it_upright(int orientation, int degrees, bool mirror) =>
        Assert.Equal((degrees, mirror), PhoneWallpaperImage.Upright(orientation));

    [Fact]
    public void A_big_png_goes_as_a_phone_sized_jpeg()
    {
        var prepared = PhoneWallpaperImage.Prepare(Png(3000, 4000));

        Assert.Equal((1440, 1920), (prepared.Width, prepared.Height));
        Assert.True(PhoneWallpaper.IsImage(prepared.Jpeg));
        Assert.Equal(0xD8, prepared.Jpeg[1]);
        var decoded = BitmapDecoder.Create(new MemoryStream(prepared.Jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal((1440, 1920), (decoded.PixelWidth, decoded.PixelHeight));
        Assert.True(prepared.Jpeg.Length <= PhoneWallpaper.MaxImageBytes);
    }

    [Fact]
    public void A_small_picture_keeps_its_size()
    {
        var prepared = PhoneWallpaperImage.Prepare(Png(720, 1600));
        Assert.Equal((720, 1600), (prepared.Width, prepared.Height));
    }

    [Fact]
    public void Something_that_is_not_a_picture_is_refused_in_the_teachers_words()
    {
        var error = Assert.Throws<InvalidDataException>(() => PhoneWallpaperImage.Prepare("isto não é uma imagem"u8.ToArray()));
        Assert.Equal(PhoneWallpaperText.NotAPicture, error.Message);
    }

    // A photo taken holding the phone upright: its pixels lie sideways and
    // EXIF says to turn them. Decoded straight to the size sent, then turned.
    [Fact]
    public void A_big_sideways_photo_is_decoded_small_and_turned_upright()
    {
        var bitmap = BitmapSource.Create(4000, 3000, 96, 96, PixelFormats.Bgr24, null, new byte[4000 * 3 * 3000], 4000 * 3);
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using var output = new MemoryStream();
        encoder.Save(output);

        var prepared = PhoneWallpaperImage.Prepare(output.ToArray());

        Assert.Equal((1440, 1920), (prepared.Width, prepared.Height));
    }

    // Only the header is read before refusing: a picture with more pixels
    // than any phone photo is never decoded.
    [Fact]
    public void A_picture_with_too_many_pixels_is_refused_before_decoding()
    {
        var error = Assert.Throws<InvalidDataException>(() => PhoneWallpaperImage.Prepare(PngHeaderOnly(30000, 30000)));
        Assert.Equal(PhoneWallpaperText.TooManyPixels, error.Message);
    }

    // A PNG that says it is width x height, with a few bytes of pixels.
    private static byte[] PngHeaderOnly(int width, int height)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // RGB
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", new byte[] { 0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01 });
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        var length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typed);
        var crc = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        png.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }

    private static byte[] Png(int width, int height)
    {
        var stride = width * 3;
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = (byte)(i % 251);
            pixels[i + 1] = 120;
            pixels[i + 2] = 200;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
