namespace Midview.Protocol;

// The payload of an InputEvent message, 11 bytes: the kind (1 byte), X and Y
// (4 bytes each) and the virtual-key code (2 bytes).
public static class InputEventCodec
{
    public static InputEvent Decode(byte[] payload)
    {
        var kind = (InputEventKind)payload[0];
        var x = BitConverter.ToInt32(payload, 1);
        var y = BitConverter.ToInt32(payload, 5);
        var vk = BitConverter.ToUInt16(payload, 9);
        return new InputEvent(kind, x, y, vk);
    }

    public static byte[] Encode(InputEvent inputEvent)
    {
        var buffer = new byte[11];
        buffer[0] = (byte)inputEvent.Kind;
        BitConverter.GetBytes(inputEvent.X).CopyTo(buffer, 1);
        BitConverter.GetBytes(inputEvent.Y).CopyTo(buffer, 5);
        BitConverter.GetBytes(inputEvent.VirtualKeyCode).CopyTo(buffer, 9);
        return buffer;
    }
}
