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
        var currentFrame = _capture.CaptureFrame();

        // The first frame has nothing to diff against. Sending every tile
        // here (instead of none) gives a newly-connected viewer an
        // immediate full picture; every later call only sends what changed.
        var tilesToSend = _previousFrame is null
            ? TileDiff.AllTiles(currentFrame.Width, currentFrame.Height)
            : TileDiff.FindChangedTiles(_previousFrame, currentFrame);

        var encoded = new List<EncodedTile>(tilesToSend.Count);
        foreach (var tile in tilesToSend)
        {
            var rect = TileDiff.TileRect(tile.Row, tile.Column, currentFrame.Width, currentFrame.Height);
            var jpegBytes = TileCodec.EncodeTile(currentFrame, rect);
            encoded.Add(new EncodedTile(tile.Row, tile.Column, jpegBytes));
        }

        _previousFrame?.Dispose();
        _previousFrame = currentFrame;
        return encoded;
    }
}
