using System.Text;
using Midview.Protocol;
using Xunit;

public class PairingTests
{
    private static readonly string Master = new('A', 40);
    private static readonly string Agent = new('B', 40);

    private static byte[] Bytes(int start) => Enumerable.Range(start, 32).Select(i => (byte)i).ToArray();

    // The reference values were computed with Python's hashlib, independently
    // of this code.
    [Fact]
    public void Code_MatchesTheReferenceValues()
    {
        Assert.Equal(433504, Pairing.Code(Master, Agent, Bytes(0), Bytes(32)));
        Assert.Equal(353409, Pairing.Code(Master, Agent, Bytes(0), Bytes(1)));
    }

    [Fact]
    public void Code_DoesNotCareAboutTheThumbprintsCase()
    {
        Assert.Equal(433504, Pairing.Code(Master.ToLowerInvariant(), Agent.ToLowerInvariant(), Bytes(0), Bytes(32)));
    }

    [Fact]
    public void Code_ChangesWithEachInput()
    {
        Assert.NotEqual(433504, Pairing.Code(new string('A', 39) + "C", Agent, Bytes(0), Bytes(32)));
        Assert.NotEqual(433504, Pairing.Code(Master, new string('B', 39) + "C", Bytes(0), Bytes(32)));
        Assert.NotEqual(433504, Pairing.Code(Master, Agent, Bytes(1), Bytes(32)));
        Assert.NotEqual(433504, Pairing.Code(Agent, Master, Bytes(0), Bytes(32)));
    }

    [Theory]
    [InlineData(42, "000 042")]
    [InlineData(433504, "433 504")]
    [InlineData(999999, "999 999")]
    public void Format_ShowsTwoGroupsOfThree(int code, string expected)
    {
        Assert.Equal(expected, Pairing.Format(code));
    }

    [Fact]
    public void Commit_IsTheNoncesSha256()
    {
        Assert.Equal(
            "630DCD2966C4336691125448BBB25B4FF412A49C732DB2C8ABC1B8581BD710DD",
            Convert.ToHexString(Pairing.Commit(Bytes(0))));
    }

    [Fact]
    public void Opens_OnlyTheCommittedNonce()
    {
        var commitment = Pairing.Commit(Bytes(0));

        Assert.True(Pairing.Opens(commitment, Bytes(0)));
        Assert.False(Pairing.Opens(commitment, Bytes(1)));
        Assert.False(Pairing.Opens(commitment, new byte[31]));
        Assert.False(Pairing.Opens(new byte[5], Bytes(0)));
    }

    [Fact]
    public void NewNonce_Is32RandomBytes()
    {
        var first = Pairing.NewNonce();

        Assert.Equal(32, first.Length);
        Assert.NotEqual(first, Pairing.NewNonce());
    }

    [Theory]
    [InlineData("PROF-01", "PROF-01")]
    [InlineData("  sala \"2\" & co ", "sala 2  co")]
    [InlineData("", "?")]
    [InlineData("\"&|", "?")]
    public void CleanTeacherName_KeepsOnlyWhatIsSafeToShow(string name, string expected)
    {
        Assert.Equal(expected, Pairing.CleanTeacherName(name));
    }

    [Fact]
    public void CleanTeacherName_IsNeverLongerThanTheLimit()
    {
        Assert.Equal(new string('x', Pairing.MaxTeacherNameLength), Pairing.CleanTeacherName(new string('x', 60)));
    }

    [Fact]
    public void ARefusal_CarriesItsReason()
    {
        var message = Pairing.Refused("Esta máquina já tem professor.");

        Assert.Equal(MessageType.PairRefused, message.Type);
        Assert.Equal("Esta máquina já tem professor.", Pairing.RefusalText(message.Payload));
    }

    [Fact]
    public void PairingResult_RoundTrips()
    {
        var result = new PairingResult("ALUNO-01", Agent, "00-1A-2B-3C-4D-5E");

        Assert.Equal(result, PairingResult.Decode(result.Encode()));
        Assert.Equal(result with { MacAddress = null }, PairingResult.Decode((result with { MacAddress = null }).Encode()));
    }

    [Fact]
    public void PairingResult_Decode_RefusesWhatIsNotOne()
    {
        Assert.Null(PairingResult.Decode(Encoding.UTF8.GetBytes("{}")));
        Assert.Null(PairingResult.Decode(Encoding.UTF8.GetBytes("isto não é json")));
        Assert.Null(PairingResult.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void PairingCodeArguments_BuildAndParse()
    {
        Assert.Equal("--pairing-code 004213 --from \"PROF 01\"", PairingCodeArguments.Build(4213, "PROF 01"));
        Assert.Equal("--pairing-code 000001 --from \"ab\"", PairingCodeArguments.Build(1, "a\"b"));
        Assert.Equal((4213, "PROF 01"), PairingCodeArguments.Parse(new[] { "--pairing-code", "004213", "--from", "PROF 01" }));
    }

    [Fact]
    public void PairingCodeArguments_Parse_RefusesAnythingElse()
    {
        Assert.Null(PairingCodeArguments.Parse(Array.Empty<string>()));
        Assert.Null(PairingCodeArguments.Parse(new[] { "--pairing-code", "12345", "--from", "x" }));
        Assert.Null(PairingCodeArguments.Parse(new[] { "--pairing-code", "12a456", "--from", "x" }));
        Assert.Null(PairingCodeArguments.Parse(new[] { new string('A', 40) }));
    }
}
