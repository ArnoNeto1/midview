namespace TreezeSuite.Protocol;

public sealed class Message
{
    public MessageType Type { get; }
    public byte[] Payload { get; }

    public Message(MessageType type, byte[] payload)
    {
        Type = type;
        Payload = payload;
    }
}
