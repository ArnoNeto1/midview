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
