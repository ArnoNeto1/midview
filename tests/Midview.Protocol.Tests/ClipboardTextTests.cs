using Midview.Protocol;
using Xunit;

public class ClipboardTextTests
{
    [Fact]
    public void EncodeThenDecode_KeepsTheTextVerbatim()
    {
        const string text = "  espaços e\nlinhas  "; // not trimmed, unlike chat text
        Assert.Equal(text, ClipboardText.Decode(ClipboardText.Encode(text)));
    }

    [Fact]
    public void Decode_Empty_IsNull() => Assert.Null(ClipboardText.Decode(System.Array.Empty<byte>()));

    [Fact]
    public void Decode_OverTheCap_IsNull()
    {
        var tooBig = new byte[ClipboardText.MaxBytes + 1];
        Assert.Null(ClipboardText.Decode(tooBig));
    }

    [Fact]
    public void Decode_AtTheCap_IsAllowed()
    {
        var text = new string('x', ClipboardText.MaxBytes);
        var payload = ClipboardText.Encode(text);
        Assert.Equal(ClipboardText.MaxBytes, payload.Length);
        Assert.Equal(text, ClipboardText.Decode(payload));
    }

    [Fact]
    public void TryEncode_ValidText_Succeeds()
    {
        Assert.True(ClipboardText.TryEncode("ola mundo", out var bytes));
        Assert.Equal("ola mundo", ClipboardText.Decode(bytes));
    }

    [Fact]
    public void TryEncode_LoneSurrogate_Fails_WithoutThrowing()
    {
        // Half of a surrogate pair (e.g. a split emoji): refused, not thrown.
        Assert.False(ClipboardText.TryEncode("quebrado \uD800 aqui", out var bytes));
        Assert.Empty(bytes);
    }

    [Fact]
    public void Decode_InvalidUtf8_IsNull()
    {
        Assert.Null(ClipboardText.Decode(new byte[] { 0xFF, 0xFE, 0xFD }));
    }
}
