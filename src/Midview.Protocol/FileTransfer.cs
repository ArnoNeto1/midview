using System.Buffers.Binary;
using System.Text;

namespace Midview.Protocol;

// Wire format for SendFile / FileChunk and the CommandAck that ends a
// transfer.
public static class FileTransfer
{
    // Plenty for what a teacher hands out (documents, images, installers),
    // and a cap on how much disk one transfer can fill on a lab machine.
    public const long MaxFileSize = 1L << 30; // 1 GiB

    // Well under MessageFramer.MaxMessageLength.
    public const int ChunkSize = 256 * 1024;

    private const int MaxNameBytes = 1024;

    // SendFile payload: file size (int64, big-endian), then the file name
    // (UTF-8) -- just a name, never a path; the receiver sanitizes it anyway.
    public static byte[] EncodeHeader(string fileName, long size)
    {
        var name = Encoding.UTF8.GetBytes(fileName);
        var header = new byte[8 + name.Length];
        BinaryPrimitives.WriteInt64BigEndian(header, size);
        name.CopyTo(header, 8);
        return header;
    }

    public static bool TryDecodeHeader(byte[] payload, out string fileName, out long size)
    {
        fileName = string.Empty;
        size = 0;
        if (payload.Length <= 8 || payload.Length > 8 + MaxNameBytes)
        {
            return false;
        }

        size = BinaryPrimitives.ReadInt64BigEndian(payload);
        if (size < 0 || size > MaxFileSize)
        {
            return false;
        }

        try
        {
            fileName = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(payload, 8, payload.Length - 8);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        return fileName.Trim().Length > 0;
    }

    // The CommandAck that ends a transfer: 1 or 0, then UTF-8 detail -- the
    // name the file was saved under, or why it wasn't.
    public static byte[] EncodeResult(bool saved, string detail)
    {
        var text = Encoding.UTF8.GetBytes(detail);
        var result = new byte[1 + text.Length];
        result[0] = (byte)(saved ? 1 : 0);
        text.CopyTo(result, 1);
        return result;
    }

    public static (bool Saved, string Detail) DecodeResult(byte[] payload) =>
        payload.Length == 0
            ? (false, string.Empty)
            : (payload[0] == 1, Encoding.UTF8.GetString(payload, 1, payload.Length - 1));
}
