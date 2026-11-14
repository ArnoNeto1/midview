using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Midview.Protocol;

namespace Midview.Master;

// The teacher's picture as the phones get it: turned upright as the
// camera's EXIF says (a photo keeps its pixels sideways and only notes how
// it was held), made no bigger than a phone's screen (FitSize) and saved as
// a JPEG of quality 90, so a 12-megapixel photo goes as 1 to 2 MB. The phone
// then scales and crops it to its own screen.
public static class PhoneWallpaperImage
{
    // The biggest phone screens, standing or lying: 1440 x 3200.
    public const int MaxShortSide = 1440;
    public const int MaxLongSide = 3200;
    public const int JpegQuality = 90;

    // A file bigger than this is no wallpaper.
    public const long MaxFileBytes = 50L * 1024 * 1024;

    public sealed record Prepared(byte[] Jpeg, int Width, int Height);

    // The size the picture is sent at: its own when it fits, or scaled down
    // keeping its shape until its shorter side is at most MaxShortSide and
    // its longer at most MaxLongSide. Never enlarged.
    public static (int Width, int Height) FitSize(int width, int height)
    {
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "a imagem não tem tamanho");
        }
        var shortSide = Math.Min(width, height);
        var longSide = Math.Max(width, height);
        var scale = Math.Min(1.0, Math.Min((double)MaxShortSide / shortSide, (double)MaxLongSide / longSide));
        if (scale >= 1.0)
        {
            return (width, height);
        }
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    // EXIF's orientation (1 to 8) as the clockwise turn, then whether to
    // mirror left to right, that brings the picture upright. Anything else
    // is taken as upright.
    public static (int Degrees, bool Mirror) Upright(int orientation) => orientation switch
    {
        2 => (0, true),
        3 => (180, false),
        4 => (180, true),
        5 => (90, true),
        6 => (90, false),
        7 => (270, true),
        8 => (270, false),
        _ => (0, false),
    };

    // The file's picture, ready to send. Throws InvalidDataException with
    // the teacher's words when it is not a picture that can be opened or is
    // too big.
    public static Prepared Prepare(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaxFileBytes)
        {
            throw new InvalidDataException(PhoneWallpaperText.FileTooBig);
        }
        return Prepare(File.ReadAllBytes(path));
    }

    // More pixels than this is not opened at all (a 108-megapixel phone
    // photo has 12000 x 9000): decoded whole, it could take gigabytes.
    public const long MaxPixels = 120_000_000;

    public static Prepared Prepare(byte[] file)
    {
        // Only the header first: the size and the EXIF orientation, no pixels.
        int storedWidth, storedHeight, orientation;
        try
        {
            using var header = new MemoryStream(file, writable: false);
            var decoder = BitmapDecoder.Create(
                header, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            var first = decoder.Frames[0];
            (storedWidth, storedHeight) = (first.PixelWidth, first.PixelHeight);
            orientation = Orientation(first);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException
            or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidDataException(PhoneWallpaperText.NotAPicture, ex);
        }
        if (storedWidth < 1 || storedHeight < 1)
        {
            throw new InvalidDataException(PhoneWallpaperText.NotAPicture);
        }
        if ((long)storedWidth * storedHeight > MaxPixels)
        {
            throw new InvalidDataException(PhoneWallpaperText.TooManyPixels);
        }

        var (degrees, mirror) = Upright(orientation);
        var turned = degrees is 90 or 270;
        var (uprightWidth, uprightHeight) = turned ? (storedHeight, storedWidth) : (storedWidth, storedHeight);
        var (width, height) = FitSize(uprightWidth, uprightHeight);

        // Then the pixels, decoded straight to the size sent (in the file's
        // own orientation), never whole at the photo's size.
        BitmapSource frame;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(file, writable: false);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
            if (width != uprightWidth || height != uprightHeight)
            {
                image.DecodePixelWidth = turned ? height : width;
                image.DecodePixelHeight = turned ? width : height;
            }
            image.EndInit();
            image.Freeze();
            frame = image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException
            or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidDataException(PhoneWallpaperText.NotAPicture, ex);
        }

        var transform = new TransformGroup();
        if (degrees != 0)
        {
            transform.Children.Add(new RotateTransform(degrees));
        }
        if (mirror)
        {
            transform.Children.Add(new ScaleTransform(-1, 1));
        }
        BitmapSource picture = transform.Children.Count == 0 ? frame : new TransformedBitmap(frame, transform);
        // The JPEG has no transparency; WIC drops it.
        picture = new FormatConvertedBitmap(picture, PixelFormats.Bgr24, null, 0);

        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var output = new MemoryStream();
        encoder.Save(output);
        var jpeg = output.ToArray();
        if (jpeg.Length > PhoneWallpaper.MaxImageBytes)
        {
            throw new InvalidDataException(PhoneWallpaperText.FileTooBig);
        }
        return new Prepared(jpeg, picture.PixelWidth, picture.PixelHeight);
    }

    // The EXIF orientation of a JPEG (1 when it has none or it can't be read).
    private static int Orientation(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort value
                ? value
                : 1;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException
            or System.Runtime.InteropServices.COMException)
        {
            return 1;
        }
    }
}
