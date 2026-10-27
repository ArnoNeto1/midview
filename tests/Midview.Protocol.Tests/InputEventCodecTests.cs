using Midview.Protocol;
using Xunit;

public class InputEventCodecTests
{
    public static IEnumerable<object[]> EveryKind() =>
        Enum.GetValues<InputEventKind>().Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Encode_then_Decode_round_trips_each_kind(InputEventKind kind)
    {
        // A negative X and a key code past 0x7FFF: the signed coordinates and
        // the whole ushort have to survive the trip.
        var inputEvent = new InputEvent(kind, X: -120, Y: 4321, VirtualKeyCode: 0xFFFE);

        Assert.Equal(inputEvent, InputEventCodec.Decode(InputEventCodec.Encode(inputEvent)));
    }

    // Every Master and Session already installed speaks this layout, and a
    // round trip alone would still pass if both ends changed together, so it
    // is pinned byte by byte: the kind as one byte, then X, Y and the key
    // code, as BitConverter writes them.
    [Theory]
    [InlineData(InputEventKind.MouseMove, 0)]
    [InlineData(InputEventKind.MouseLeftDown, 1)]
    [InlineData(InputEventKind.MouseLeftUp, 2)]
    [InlineData(InputEventKind.KeyDown, 3)]
    [InlineData(InputEventKind.KeyUp, 4)]
    [InlineData(InputEventKind.MouseRightDown, 5)]
    [InlineData(InputEventKind.MouseRightUp, 6)]
    [InlineData(InputEventKind.MouseWheel, 7)]
    public void Encode_writes_the_11_byte_wire_layout(InputEventKind kind, byte kindByte)
    {
        var encoded = InputEventCodec.Encode(new InputEvent(kind, X: 0x01020304, Y: -2, VirtualKeyCode: 0x0A0B));

        Assert.Equal(new byte[] { kindByte, 0x04, 0x03, 0x02, 0x01, 0xFE, 0xFF, 0xFF, 0xFF, 0x0B, 0x0A }, encoded);
    }

    // Decode has no length check of its own: a payload shorter than the 11
    // bytes of the layout is refused because BitConverter throws on it. A
    // longer one isn't refused; its first 11 bytes are read.
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)]
    public void Decode_rejects_a_wrong_length_payload(int length)
    {
        Assert.ThrowsAny<Exception>(() => InputEventCodec.Decode(new byte[length]));
    }
}
