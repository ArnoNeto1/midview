using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Midview.Protocol;

namespace Midview.Master;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

// What the window draws: the whole picture (32 bits a pixel: blue, green,
// red, unused; Stride bytes a row) and the parts of it that changed since it
// last drew. Resized means the picture's size changed, so it all changed.
public sealed record ScreenPicture(
    byte[] Pixels, int Width, int Height, int Stride, bool Resized, IReadOnlyList<PixelRect> Changed);

// The student's screen as "Ver a tela" has received it, put together on the
// receiving thread. Decoding every JPEG on the window's own thread, one tile
// at a time, was what held the picture to a few frames a second whenever
// much of the screen changed; now the window only copies what changed, once
// for however many updates arrived since it last drew (TakeChanges).
public sealed class RemoteScreen
{
    // Past this many separate changed parts, one rectangle around all of
    // them is copied instead.
    private const int MaxChangedParts = 32;

    private readonly object _gate = new();
    private readonly List<PixelRect> _changed = new();
    private byte[] _pixels = Array.Empty<byte>();
    private int _width;
    private int _height;
    private bool _resized;
    private int _frameToAcknowledge;

    // A ScreenFrame from a current Session, which says its screen's size.
    public void AddFrame(ScreenFrame frame)
    {
        // Decoded before taking the lock, so the window never waits on it,
        // and all pasted at once, so it never draws half an update.
        // ScreenFrame.Decode keeps the regions' area to about twice the
        // screen's, which bounds what this holds at a time.
        var decoded = new List<(ScreenRegion Region, DecodedImage? Image)>(frame.Regions.Count);
        try
        {
            foreach (var region in frame.Regions)
            {
                decoded.Add((region, Decode(region.Jpeg, region.Width, region.Height)));
            }

            lock (_gate)
            {
                if (frame.ScreenWidth != _width || frame.ScreenHeight != _height)
                {
                    Resize(frame.ScreenWidth, frame.ScreenHeight, keepPicture: false);
                }

                foreach (var (region, image) in decoded)
                {
                    if (image is not null)
                    {
                        Paste(image, region.X, region.Y, region.Width, region.Height);
                    }
                }
                _frameToAcknowledge = frame.FrameId;
            }
        }
        finally
        {
            foreach (var (_, image) in decoded)
            {
                image?.Dispose();
            }
        }
    }

    // A FrameTile from a Session from before ScreenFrame, which never says
    // its screen's size: the picture grows to fit the tiles that arrive.
    public void AddTile(int row, int column, byte[] jpeg)
    {
        if (row < 0 || column < 0 || row >= ScreenFrame.MaxScreenSide / TileDiff.TileSize
            || column >= ScreenFrame.MaxScreenSide / TileDiff.TileSize)
        {
            return;
        }

        using var image = Decode(jpeg, TileDiff.TileSize, TileDiff.TileSize);
        if (image is null)
        {
            return;
        }

        var x = column * TileDiff.TileSize;
        var y = row * TileDiff.TileSize;
        lock (_gate)
        {
            if (x + image.Width > _width || y + image.Height > _height)
            {
                var width = Math.Max(_width, x + image.Width);
                var height = Math.Max(_height, y + image.Height);
                if (!ScreenFrame.IsPlausibleScreen(width, height))
                {
                    return;
                }
                Resize(width, height, keepPicture: true);
            }
            Paste(image, x, y, image.Width, image.Height);
        }
    }

    // Calls draw with what changed since the last call, while nothing else
    // can change the picture, and nothing at all when nothing changed.
    // Returns the newest ScreenFrame drawn, for the window to acknowledge,
    // or 0 when there's none to acknowledge.
    public int TakeChanges(Action<ScreenPicture> draw)
    {
        lock (_gate)
        {
            if (_changed.Count > 0)
            {
                draw(new ScreenPicture(_pixels, _width, _height, _width * 4, _resized, _changed.ToList()));
            }

            var frameId = _frameToAcknowledge;
            _changed.Clear();
            _resized = false;
            _frameToAcknowledge = 0;
            return frameId;
        }
    }

    private void Resize(int width, int height, bool keepPicture)
    {
        var pixels = new byte[width * height * 4];
        if (keepPicture)
        {
            for (var y = 0; y < Math.Min(_height, height); y++)
            {
                Buffer.BlockCopy(_pixels, y * _width * 4, pixels, y * width * 4, Math.Min(_width, width) * 4);
            }
        }

        _pixels = pixels;
        _width = width;
        _height = height;
        _resized = true;
        _changed.Clear();
        _changed.Add(new PixelRect(0, 0, width, height));
    }

    // The part of the image that fits both the region it was sent for and
    // the picture, at (x, y).
    private void Paste(DecodedImage image, int x, int y, int width, int height)
    {
        width = Math.Min(Math.Min(width, image.Width), _width - x);
        height = Math.Min(Math.Min(height, image.Height), _height - y);
        if (x < 0 || y < 0 || width <= 0 || height <= 0)
        {
            return;
        }

        for (var row = 0; row < height; row++)
        {
            Buffer.BlockCopy(image.Pixels, row * image.Width * 4, _pixels, ((y + row) * _width + x) * 4, width * 4);
        }
        MarkChanged(new PixelRect(x, y, width, height));
    }

    private void MarkChanged(PixelRect rect)
    {
        if (_changed.Count < MaxChangedParts)
        {
            _changed.Add(rect);
            return;
        }

        var left = Math.Min(rect.X, _changed.Min(part => part.X));
        var top = Math.Min(rect.Y, _changed.Min(part => part.Y));
        var right = Math.Max(rect.X + rect.Width, _changed.Max(part => part.X + part.Width));
        var bottom = Math.Max(rect.Y + rect.Height, _changed.Max(part => part.Y + part.Height));
        _changed.Clear();
        _changed.Add(new PixelRect(left, top, right - left, bottom - top));
    }

    // Null when the bytes aren't an image this can decode, or one bigger
    // than the region it was sent for: one bad region or tile is skipped,
    // not the whole picture. The size comes from the JPEG's header before
    // anything is decoded, so a header claiming a huge image costs nothing.
    private static DecodedImage? Decode(byte[] jpeg, int maxWidth, int maxHeight)
    {
        try
        {
            using var stream = new MemoryStream(jpeg);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            if (image is not Bitmap bitmap || bitmap.Width > maxWidth || bitmap.Height > maxHeight)
            {
                return null;
            }

            var width = bitmap.Width;
            var height = bitmap.Height;
            var pixels = ArrayPool<byte>.Shared.Rent(width * 4 * height);
            var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                // Converted to 32 bits a pixel straight into the rented array,
                // instead of into a buffer of GDI+'s own that gets copied again.
                var target = new BitmapData
                {
                    Width = width,
                    Height = height,
                    Stride = width * 4,
                    PixelFormat = PixelFormat.Format32bppRgb,
                    Scan0 = pinned.AddrOfPinnedObject(),
                };
                bitmap.LockBits(
                    new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly | ImageLockMode.UserInputBuffer,
                    PixelFormat.Format32bppRgb, target);
                bitmap.UnlockBits(target);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(pixels);
                throw;
            }
            finally
            {
                pinned.Free();
            }
            return new DecodedImage(pixels, width, height);
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OverflowException or OutOfMemoryException)
        {
            return null;
        }
    }

    // Pixels is rented: Width * 4 bytes a row, possibly longer than needed.
    private sealed class DecodedImage : IDisposable
    {
        public DecodedImage(byte[] pixels, int width, int height)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
        }

        public byte[] Pixels { get; }
        public int Width { get; }
        public int Height { get; }

        public void Dispose() => ArrayPool<byte>.Shared.Return(Pixels);
    }
}
