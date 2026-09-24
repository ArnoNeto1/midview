using System.Buffers.Binary;
using System.Text;

namespace Midview.Protocol;

// Wire format for SendFile / FileChunk. Both CommandAcks of a transfer
// (accepted or refused, then saved or not) carry a CommandResult: on
// success, the detail is the name the file was saved under.
public static class FileTransfer
{
    // Plenty for what a teacher hands out (documents, images, installers),
    // and a cap on how much disk one transfer can fill on a lab machine.
    public const long MaxFileSize = 1L << 30; // 1 GiB

    // Well under MessageFramer.MaxMessageLength.
    public const int ChunkSize = 256 * 1024;

    public const int MaxNameBytes = 1024;

    // SendFile payload: file size (int64, big-endian), then the file name
    // (UTF-8) -- just a name from the teacher, a relative path when
    // collecting (see FileCollection); the receiver sanitizes it either way.
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
}
