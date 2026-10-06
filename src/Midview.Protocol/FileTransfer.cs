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

    // SendFileWithOptions payload: overwrite (one byte, 0 or 1), the
    // AfterSend action (one byte), then a SendFile header. Its own message
    // type, so a Session from before the options never mistakes it for a
    // file with a mangled name.
    public static byte[] EncodeOptionsHeader(SendFileOptions options, string fileName, long size)
    {
        var header = EncodeHeader(fileName, size);
        var payload = new byte[2 + header.Length];
        payload[0] = (byte)(options.Overwrite ? 1 : 0);
        payload[1] = (byte)options.AfterSend;
        header.CopyTo(payload, 2);
        return payload;
    }

    public static bool TryDecodeOptionsHeader(byte[] payload, out SendFileOptions options, out string fileName, out long size)
    {
        options = SendFileOptions.Default;
        fileName = string.Empty;
        size = 0;
        if (payload.Length < 2 || payload[0] > 1 || !Enum.IsDefined((AfterSend)payload[1]))
        {
            return false;
        }

        options = new SendFileOptions(payload[0] == 1, (AfterSend)payload[1]);
        return TryDecodeHeader(payload[2..], out fileName, out size);
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
