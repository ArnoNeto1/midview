using System.Buffers.Binary;

namespace Midview.Protocol;

public static class MessageFramer
{
    // Far above anything real (a 64x64 JPEG tile is a few KB); stops a bad or
    // hostile length prefix from making Read allocate up to 2 GB.
    public const int MaxMessageLength = 16 * 1024 * 1024;

    // One write per message. With separate writes for the length, the type
    // and the payload, a message with an empty payload ended in a zero-length
    // write, which SslStream on Windows sends as an empty TLS record. Read
    // never consumes that record, so a side that answered and closed still
    // had unread data -- and Windows then resets the connection instead of
    // closing it, discarding whatever the other side hadn't read yet (seen
    // on CollectFiles and the Agent's BlockInternet/UnblockInternet). It also
    // makes every message one record instead of three.
    public static void Write(Stream stream, Message message)
    {
        var frame = new byte[5 + message.Payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, message.Payload.Length + 1);
        frame[4] = (byte)message.Type;
        message.Payload.CopyTo(frame, 5);
        stream.Write(frame, 0, frame.Length);
        stream.Flush();
    }

    public static Message Read(Stream stream)
    {
        var header = ReadExact(stream, 4);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 1 || length > MaxMessageLength)
        {
            throw new InvalidDataException($"Invalid message length {length}");
        }

        var typeAndPayload = ReadExact(stream, length);
        var type = (MessageType)typeAndPayload[0];
        var payload = typeAndPayload[1..];
        return new Message(type, payload);
    }

    private static byte[] ReadExact(Stream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("Connection closed while reading message");
            }
            offset += read;
        }
        return buffer;
    }
}
