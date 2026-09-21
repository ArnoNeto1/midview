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

        if (_previousFrame is null)
        {
            _previousFrame = currentFrame;
            return Array.Empty<EncodedTile>();
        }

        var changedTiles = TileDiff.FindChangedTiles(_previousFrame, currentFrame);
        var encoded = new List<EncodedTile>(changedTiles.Count);
        foreach (var tile in changedTiles)
        {
            var rect = TileDiff.TileRect(tile.Row, tile.Column, currentFrame.Width, currentFrame.Height);
            var jpegBytes = TileCodec.EncodeTile(currentFrame, rect);
            encoded.Add(new EncodedTile(tile.Row, tile.Column, jpegBytes));
        }

        _previousFrame.Dispose();
        _previousFrame = currentFrame;
        return encoded;
    }
}
