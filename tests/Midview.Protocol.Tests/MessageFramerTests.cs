using Midview.Protocol;
using Xunit;

public class MessageFramerTests
{
    [Fact]
    public void WriteThenRead_RoundTripsMessage()
    {
        var stream = new MemoryStream();
        var original = new Message(MessageType.Heartbeat, new byte[] { 1, 2, 3 });

        MessageFramer.Write(stream, original);
        stream.Position = 0;
        var result = MessageFramer.Read(stream);

        Assert.Equal(MessageType.Heartbeat, result.Type);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Payload);
    }

    [Fact]
    public void Read_WithALimit_RefusesALongerMessage_BeforeReadingIt()
    {
        var stream = new MemoryStream();
        MessageFramer.Write(stream, new Message(MessageType.InviteJoin, new byte[100]));

        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => MessageFramer.Read(stream, 100));

        stream.Position = 0;
        Assert.Equal(100, MessageFramer.Read(stream, 101).Payload.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100_000)]
    public void Write_IsASingleNonEmptyWrite(int payloadLength)
    {
        // A zero-length write becomes an empty TLS record on Windows, which
        // nobody reads and which then resets the connection on close.
        var stream = new WriteRecordingStream();

        MessageFramer.Write(stream, new Message(MessageType.CollectFiles, new byte[payloadLength]));

        Assert.Equal(new[] { 5 + payloadLength }, stream.WriteLengths);
    }

    [Fact]
    public void Read_EmptyPayload_RoundTrips()
    {
        var stream = new MemoryStream();
        MessageFramer.Write(stream, new Message(MessageType.Hello, Array.Empty<byte>()));
        stream.Position = 0;

        var result = MessageFramer.Read(stream);

        Assert.Equal(MessageType.Hello, result.Type);
        Assert.Empty(result.Payload);
    }

    [Fact]
    public void Read_TruncatedStream_ThrowsEndOfStream()
    {
        var stream = new MemoryStream();
        MessageFramer.Write(stream, new Message(MessageType.Heartbeat, new byte[] { 1, 2, 3 }));
        stream.SetLength(stream.Length - 2);
        stream.Position = 0;

        Assert.Throws<EndOfStreamException>(() => MessageFramer.Read(stream));
    }

    [Fact]
    public void Read_LengthAboveMaximum_ThrowsInsteadOfAllocating()
    {
        var header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, MessageFramer.MaxMessageLength + 1);
        var stream = new MemoryStream(header);

        Assert.Throws<InvalidDataException>(() => MessageFramer.Read(stream));
    }

    // Records the length of every write, the way SslStream would turn each
    // one into its own TLS record.
    private sealed class WriteRecordingStream : Stream
    {
        public List<int> WriteLengths { get; } = new();

        public override void Write(byte[] buffer, int offset, int count) => WriteLengths.Add(count);
        public override void Write(ReadOnlySpan<byte> buffer) => WriteLengths.Add(buffer.Length);
        public override void WriteByte(byte value) => WriteLengths.Add(1);
        public override void Flush()
        {
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
