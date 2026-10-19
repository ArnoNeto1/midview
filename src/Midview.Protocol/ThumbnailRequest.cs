using System.Buffers.Binary;

namespace Midview.Protocol;

// Payload of MessageType.RequestThumbnail: the box the thumbnail must fit
// in, as big as the tile the teacher's grid shows it in -- width, then
// height (uint16 each, big-endian). The Session keeps the screen's aspect
// ratio inside it and never scales up. A Master from before tiles could be
// resized sends it empty, and gets the size those tiles always had.
public static class ThumbnailRequest
{
    public const int DefaultWidth = 320;
    public const int DefaultHeight = 180;

    // The largest tile, on a screen at 150%: enough to stay sharp, and
    // still a small JPEG every few seconds per machine.
    public const int MaxWidth = 960;
    public const int MaxHeight = 540;

    public static byte[] Encode(int width, int height)
    {
        if (width is < 1 or > MaxWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (height is < 1 or > MaxHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var payload = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)width);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), (ushort)height);
        return payload;
    }

    // Never refuses: a preview always gets a thumbnail, at the default size
    // when the request can't be read, and never past the maximum.
    public static (int Width, int Height) Decode(byte[] payload)
    {
        if (payload.Length != 4)
        {
            return (DefaultWidth, DefaultHeight);
        }

        int width = BinaryPrimitives.ReadUInt16BigEndian(payload);
        int height = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(2));
        if (width == 0 || height == 0)
        {
            return (DefaultWidth, DefaultHeight);
        }
        return (Math.Min(width, MaxWidth), Math.Min(height, MaxHeight));
    }
}
