using System.Drawing;
using System.Drawing.Imaging;

namespace Midview.Protocol;

public static class TileCodec
{
    private static readonly ImageCodecInfo JpegEncoder = ImageCodecInfo.GetImageEncoders()
        .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

    public static byte[] EncodeTile(Bitmap source, Rectangle rect, long quality = 90L)
    {
        using var stream = new MemoryStream();
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);

        // The whole image (a full-screen update, a thumbnail) needs no copy,
        // and neither does part of a 32-bit one (every screen capture): a
        // bitmap over the source's own pixels gives the same JPEG as a Clone,
        // without copying up to 8 MB first.
        if (rect == new Rectangle(Point.Empty, source.Size))
        {
            source.Save(stream, JpegEncoder, encoderParams);
        }
        else if (source.PixelFormat is PixelFormat.Format32bppArgb or PixelFormat.Format32bppRgb or PixelFormat.Format32bppPArgb)
        {
            var data = source.LockBits(rect, ImageLockMode.ReadOnly, source.PixelFormat);
            try
            {
                using var view = new Bitmap(rect.Width, rect.Height, data.Stride, source.PixelFormat, data.Scan0);
                view.Save(stream, JpegEncoder, encoderParams);
            }
            finally
            {
                source.UnlockBits(data);
            }
        }
        else
        {
            using var tile = source.Clone(rect, source.PixelFormat);
            tile.Save(stream, JpegEncoder, encoderParams);
        }
        return stream.ToArray();
    }

    public static Bitmap DecodeTile(byte[] jpegBytes)
    {
        using var stream = new MemoryStream(jpegBytes);
        return new Bitmap(stream);
    }
}
