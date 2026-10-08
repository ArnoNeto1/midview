using System.Buffers.Binary;

namespace Midview.Protocol;

// One region of a ScreenFrame: a JPEG to draw at (X, Y), Width x Height.
public sealed record ScreenRegion(int X, int Y, int Width, int Height, byte[] Jpeg);

// One update of the student's screen as a single message
// (MessageType.ScreenFrame), in place of one FrameTile per 64x64 tile: the
// screen's size, so the viewer sets up its picture once, and the regions that
// changed since the last update. The viewer acknowledges each one it has
// drawn (FrameAck, with FrameId), and the Session keeps only a couple of
// updates unacknowledged at a time (see FrameCredit).
//
// Payload, little-endian: frame id, screen width, screen height, region
// count (int32 each); then per region x, y, width, height, JPEG length
// (int32 each) and the JPEG.
public sealed record ScreenFrame(int FrameId, int ScreenWidth, int ScreenHeight, IReadOnlyList<ScreenRegion> Regions)
{
    // Beyond any real monitor (8K is 7680x4320); stops a bad header from
    // making the viewer set up a picture of gigabytes.
    public const int MaxScreenSide = 16384;
    public const long MaxScreenPixels = 8192L * 8192;

    // The JPEG bytes one message carries at most, well under
    // MessageFramer.MaxMessageLength: a bigger update goes as several
    // messages (Split). Only screens beyond 4K ever get near it.
    public const int MaxJpegBytesPerMessage = 8 * 1024 * 1024;

    private const int HeaderLength = 16;
    private const int RegionHeaderLength = 20;

    public static bool IsPlausibleScreen(long width, long height) =>
        width is >= 1 and <= MaxScreenSide && height is >= 1 and <= MaxScreenSide && width * height <= MaxScreenPixels;

    // The regions of one update in groups of at most MaxJpegBytesPerMessage
    // of JPEG each (a region bigger than that on its own goes alone), one
    // ScreenFrame per group.
    public static IReadOnlyList<IReadOnlyList<ScreenRegion>> Split(IReadOnlyList<ScreenRegion> regions)
    {
        var groups = new List<IReadOnlyList<ScreenRegion>>();
        var group = new List<ScreenRegion>();
        var bytes = 0L;
        foreach (var region in regions)
        {
            if (group.Count > 0 && bytes + region.Jpeg.Length > MaxJpegBytesPerMessage)
            {
                groups.Add(group);
                group = new List<ScreenRegion>();
                bytes = 0;
            }
            group.Add(region);
            bytes += region.Jpeg.Length;
        }
        if (group.Count > 0)
        {
            groups.Add(group);
        }
        return groups;
    }

    public byte[] Encode()
    {
        var buffer = new byte[HeaderLength + Regions.Sum(region => RegionHeaderLength + region.Jpeg.Length)];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, FrameId);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], ScreenWidth);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], ScreenHeight);
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], Regions.Count);
        var offset = HeaderLength;
        foreach (var region in Regions)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[offset..], region.X);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 4)..], region.Y);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 8)..], region.Width);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 12)..], region.Height);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 16)..], region.Jpeg.Length);
            region.Jpeg.CopyTo(span[(offset + RegionHeaderLength)..]);
            offset += RegionHeaderLength + region.Jpeg.Length;
        }
        return buffer;
    }

    // Null when the payload isn't a well-formed frame, or names a region
    // outside the screen it describes.
    public static ScreenFrame? Decode(byte[] payload)
    {
        if (payload.Length < HeaderLength)
        {
            return null;
        }

        var span = payload.AsSpan();
        var frameId = BinaryPrimitives.ReadInt32LittleEndian(span);
        var screenWidth = BinaryPrimitives.ReadInt32LittleEndian(span[4..]);
        var screenHeight = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(span[12..]);
        if (!IsPlausibleScreen(screenWidth, screenHeight)
            || count < 0 || count > (payload.Length - HeaderLength) / RegionHeaderLength)
        {
            return null;
        }

        // TileRegions never sends more than twice the screen's area in one
        // update (it adds unchanged tiles only up to as many as changed), so
        // anything beyond that is no Session's update, and decoding it all
        // would only cost the viewer time and memory.
        var maxArea = 2L * RoundUpToTiles(screenWidth) * RoundUpToTiles(screenHeight);
        var area = 0L;
        var regions = new List<ScreenRegion>(count);
        var offset = HeaderLength;
        for (var i = 0; i < count; i++)
        {
            if (payload.Length - offset < RegionHeaderLength)
            {
                return null;
            }

            var x = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
            var y = BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 4)..]);
            var width = BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 8)..]);
            var height = BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 12)..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 16)..]);
            offset += RegionHeaderLength;
            if (x < 0 || y < 0 || width < 1 || height < 1
                || width > screenWidth - x || height > screenHeight - y
                || length < 0 || length > payload.Length - offset
                || (area += (long)width * height) > maxArea)
            {
                return null;
            }

            regions.Add(new ScreenRegion(x, y, width, height, payload[offset..(offset + length)]));
            offset += length;
        }

        return offset == payload.Length ? new ScreenFrame(frameId, screenWidth, screenHeight, regions) : null;
    }

    private static long RoundUpToTiles(int pixels) =>
        (pixels + (long)TileDiff.TileSize - 1) / TileDiff.TileSize * TileDiff.TileSize;

    // FrameAck's payload: the newest frame the viewer has drawn, which also
    // acknowledges every frame before it.
    public static byte[] EncodeAck(int frameId) => BitConverter.GetBytes(frameId);

    public static int? DecodeAck(byte[] payload) =>
        payload.Length == 4 ? BitConverter.ToInt32(payload, 0) : null;
}
