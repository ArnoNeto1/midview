using Midview.Protocol;
using Xunit;

public class CommandResultTests
{
    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        Assert.Equal((true, "notas (2).pdf"), CommandResult.Decode(CommandResult.Encode(true, "notas (2).pdf")));
        Assert.Equal((false, "sem espaço"), CommandResult.Decode(CommandResult.Encode(false, "sem espaço")));
    }

    [Fact]
    public void Decode_EmptyPayload_IsAFailureWithoutDetail()
    {
        Assert.Equal((false, string.Empty), CommandResult.Decode(Array.Empty<byte>()));
    }
}
