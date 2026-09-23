using System.Buffers.Binary;

namespace Midview.Protocol;

public static class MessageFramer
{
    // Far above anything real (a 64x64 JPEG tile is a few KB); stops a bad or
    // hostile length prefix from making Read allocate up to 2 GB.
    public const int MaxMessageLength = 16 * 1024 * 1024;

    public static void Write(Stream stream, Message message)
    {
        var length = message.Payload.Length + 1;
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, length);
        stream.Write(header, 0, 4);
        stream.WriteByte((byte)message.Type);
        stream.Write(message.Payload, 0, message.Payload.Length);
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
