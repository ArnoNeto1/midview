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
}
