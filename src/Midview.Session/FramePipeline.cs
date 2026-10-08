using System.Drawing;
using Midview.Protocol;

namespace Midview.Session;

// What changed on the screen since the last update: its size and the changed
// regions, one JPEG each. No regions when nothing changed.
public sealed record ScreenUpdate(int Width, int Height, IReadOnlyList<ScreenRegion> Regions);

public sealed class FramePipeline
{
    private readonly IScreenCapture _capture;
    private Bitmap? _previousFrame;

    // The frame before _previousFrame, captured into again next time when the
    // capture can do that (IReusableScreenCapture).
    private Bitmap? _spareFrame;

    public FramePipeline(IScreenCapture capture)
    {
        _capture = capture;
    }

    // For a viewer from before ScreenFrame: one JPEG per changed tile.
    public IReadOnlyList<EncodedTile> CaptureNextFrame()
    {
        var changes = CaptureChanges();
        var frame = changes.Frame;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var encoded = new List<EncodedTile>(changes.Tiles.Count);
        var totalBytes = 0;
        foreach (var tile in changes.Tiles)
        {
            var rect = TileDiff.TileRect(tile.Row, tile.Column, frame.Width, frame.Height);
            var jpegBytes = TileCodec.EncodeTile(frame, rect);
            totalBytes += jpegBytes.Length;
            encoded.Add(new EncodedTile(tile.Row, tile.Column, jpegBytes));
        }

        Log(changes, sw.Elapsed.TotalMilliseconds, $"tiles={changes.Tiles.Count} bytes={totalBytes}");
        Keep(frame);
        return encoded;
    }

    // What changed since the last call, merged into a few regions (see
    // TileRegions).
    public ScreenUpdate CaptureNextUpdate()
    {
        var changes = CaptureChanges();
        var frame = changes.Frame;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var regions = new List<ScreenRegion>();
        var totalBytes = 0;
        foreach (var rect in TileRegions.Merge(changes.Tiles, frame.Width, frame.Height))
        {
            var jpegBytes = TileCodec.EncodeTile(frame, rect);
            totalBytes += jpegBytes.Length;
            regions.Add(new ScreenRegion(rect.X, rect.Y, rect.Width, rect.Height, jpegBytes));
        }

        Log(changes, sw.Elapsed.TotalMilliseconds,
            $"tiles={changes.Tiles.Count} regions={regions.Count} bytes={totalBytes}");
        var update = new ScreenUpdate(frame.Width, frame.Height, regions);
        Keep(frame);
        return update;
    }

    private Changes CaptureChanges()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var currentFrame = Capture();
        var captureMs = sw.Elapsed.TotalMilliseconds;

        // The first frame has nothing to diff against. Sending every tile
        // here (instead of none) gives a newly-connected viewer an
        // immediate full picture; every later call only sends what changed.
        // A resolution change counts as a first frame too: TileDiff can't
        // compare different sizes, and every tile position moved anyway.
        sw.Restart();
        var isFullFrame = _previousFrame is null
            || _previousFrame.Width != currentFrame.Width
            || _previousFrame.Height != currentFrame.Height;
        var tilesToSend = isFullFrame
            ? TileDiff.AllTiles(currentFrame.Width, currentFrame.Height)
            : TileDiff.FindChangedTiles(_previousFrame!, currentFrame);
        return new Changes(currentFrame, tilesToSend, captureMs, sw.Elapsed.TotalMilliseconds);
    }

    private Bitmap Capture()
    {
        if (_capture is IReusableScreenCapture reusable && _spareFrame is not null)
        {
            var spare = _spareFrame;
            if (reusable.TryCaptureInto(spare))
            {
                _spareFrame = null;
                return spare;
            }
        }

        return _capture.CaptureFrame();
    }

    // The frame just captured becomes the one the next call diffs against.
    private void Keep(Bitmap frame)
    {
        if (_capture is IReusableScreenCapture)
        {
            _spareFrame?.Dispose();
            _spareFrame = _previousFrame;
        }
        else
        {
            _previousFrame?.Dispose();
        }
        _previousFrame = frame;
    }

    private static void Log(Changes changes, double encodeMs, string sent) =>
        SessionDiagnostics.Log(
            $"frame {changes.Frame.Width}x{changes.Frame.Height} capture={changes.CaptureMs:F1}ms " +
            $"diff={changes.DiffMs:F1}ms encode={encodeMs:F1}ms {sent}");

    private sealed record Changes(Bitmap Frame, IReadOnlyList<TileCoord> Tiles, double CaptureMs, double DiffMs);
}
