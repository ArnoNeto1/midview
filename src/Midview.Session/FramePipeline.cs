using System.Drawing;
using Midview.Protocol;

namespace Midview.Session;

public sealed class FramePipeline
{
    private readonly IScreenCapture _capture;
    private Bitmap? _previousFrame;

    public FramePipeline(IScreenCapture capture)
    {
        _capture = capture;
    }

    public IReadOnlyList<EncodedTile> CaptureNextFrame()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var currentFrame = _capture.CaptureFrame();
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
        var diffMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var encoded = new List<EncodedTile>(tilesToSend.Count);
        var totalBytes = 0;
        foreach (var tile in tilesToSend)
        {
            var rect = TileDiff.TileRect(tile.Row, tile.Column, currentFrame.Width, currentFrame.Height);
            var jpegBytes = TileCodec.EncodeTile(currentFrame, rect);
            totalBytes += jpegBytes.Length;
            encoded.Add(new EncodedTile(tile.Row, tile.Column, jpegBytes));
        }
        var encodeMs = sw.Elapsed.TotalMilliseconds;

        SessionDiagnostics.Log(
            $"frame {currentFrame.Width}x{currentFrame.Height} capture={captureMs:F1}ms diff={diffMs:F1}ms " +
            $"encode={encodeMs:F1}ms tiles={tilesToSend.Count} bytes={totalBytes}");

        _previousFrame?.Dispose();
        _previousFrame = currentFrame;
        return encoded;
    }
}
