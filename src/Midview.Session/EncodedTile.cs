namespace Midview.Session;

public sealed record EncodedTile(int Row, int Column, byte[] JpegBytes);
