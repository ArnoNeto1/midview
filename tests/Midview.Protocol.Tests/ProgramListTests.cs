using Midview.Protocol;
using Xunit;

public class ProgramListTests
{
    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        var names = new[] { "Google Chrome", "7-Zip 23.01", "Acentuação çãó" };

        Assert.True(ProgramList.TryDecode(ProgramList.Encode(names), out var decoded));
        Assert.Equal(names, decoded);
    }

    [Fact]
    public void Encode_TrimsBlanksAndEmpties()
    {
        Assert.True(ProgramList.TryDecode(ProgramList.Encode(new[] { "  App  ", "", "   " }), out var decoded));
        Assert.Equal(new[] { "App" }, decoded);
    }

    [Fact]
    public void EncodeThenDecode_Empty_IsEmpty()
    {
        Assert.True(ProgramList.TryDecode(ProgramList.Encode(Array.Empty<string>()), out var decoded));
        Assert.Empty(decoded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Decode_TooShort_IsFalse(int length) => Assert.False(ProgramList.TryDecode(new byte[length], out _));

    [Fact]
    public void Decode_WrongVersion_IsFalse()
    {
        var payload = ProgramList.Encode(new[] { "App" });
        payload[0] = 2;
        Assert.False(ProgramList.TryDecode(payload, out _));
    }

    [Fact]
    public void Decode_ExtraTrailingBytes_IsFalse()
    {
        var payload = ProgramList.Encode(new[] { "App" }).Concat(new byte[] { 0 }).ToArray();
        Assert.False(ProgramList.TryDecode(payload, out _));
    }

    [Fact]
    public void Encode_AVeryLongMultibyteName_StillDecodes()
    {
        // 200 'ç' = 400 UTF-8 bytes, over the 255 limit, and the cut lands
        // between the two bytes of a 'ç' if not done on a character boundary.
        var name = new string('ç', 200);

        Assert.True(ProgramList.TryDecode(ProgramList.Encode(new[] { name }), out var decoded));
        var only = Assert.Single(decoded);
        Assert.StartsWith("ç", only);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(only) <= ProgramList.MaxNameLength);
    }

    [Fact]
    public void Decode_LengthPastEnd_IsFalse()
    {
        // count=1, then a length byte claiming more than is there.
        var payload = new byte[] { ProgramList.FormatVersion, 0, 1, 50, (byte)'A' };
        Assert.False(ProgramList.TryDecode(payload, out _));
    }
}
