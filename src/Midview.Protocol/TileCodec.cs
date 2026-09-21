using System.Drawing;
using System.Drawing.Imaging;

namespace Midview.Protocol;

public static class TileCodec
{
    public static byte[] EncodeTile(Bitmap source, Rectangle rect, long quality = 75L)
    {
        using var tile = source.Clone(rect, source.PixelFormat);
        using var stream = new MemoryStream();
        var jpegEncoder = ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        tile.Save(stream, jpegEncoder, encoderParams);
        return stream.ToArray();
    }

    public static Bitmap DecodeTile(byte[] jpegBytes)
    {
        using var stream = new MemoryStream(jpegBytes);
        return new Bitmap(stream);
    }
}
