using System.Text;
using Midview.Protocol;
using Xunit;

public class NewTeacherInviteTests
{
    private static readonly string Master = new('A', 40);
    private static readonly string Agent = new('D', 40);
    private static readonly byte[] AgentNonce = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] MasterNonce = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();

    private static byte[] ProofFor(string code, string master = "", string agent = "") =>
        NewTeacherInvite.Proof(
            NewTeacherInvite.Key(code, agent.Length > 0 ? agent : Agent),
            master.Length > 0 ? master : Master, agent.Length > 0 ? agent : Agent, AgentNonce, MasterNonce);

    [Fact]
    public void NewCode_IsEightCharactersOfTheAlphabet()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => NewTeacherInvite.NewCode()).ToList();

        Assert.All(codes, code => Assert.True(NewTeacherInvite.IsCode(code)));
        Assert.True(codes.Distinct().Count() > 45);
    }

    [Theory]
    [InlineData("K7PQ 2M94", "K7PQ2M94")]
    [InlineData("k7pq-2m94", "K7PQ2M94")]
    [InlineData(" kopq 2m9l ", "K0PQ2M91")]
    [InlineData("I7PQ2M94", "17PQ2M94")]
    public void TryRead_TakesWhatATeacherTypes(string typed, string expected)
    {
        Assert.True(NewTeacherInvite.TryRead(typed, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("K7PQ2M9")]
    [InlineData("K7PQ2M945")]
    [InlineData("K7PQ2M9U")]
    [InlineData("K7PQ2M9!")]
    public void TryRead_RefusesAnythingElse(string typed)
    {
        Assert.False(NewTeacherInvite.TryRead(typed, out var code));
        Assert.Equal(string.Empty, code);
    }

    [Fact]
    public void Format_SplitsTheCodeInTwo() => Assert.Equal("K7PQ 2M94", NewTeacherInvite.Format("K7PQ2M94"));

    [Fact]
    public void AllowNewTeacherPayload_RoundTrips()
    {
        Assert.Equal("K7PQ2M94", NewTeacherInvite.Decode(NewTeacherInvite.Encode("K7PQ2M94")));
        Assert.Equal(8, NewTeacherInvite.Encode("K7PQ2M94").Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("K7PQ2M9")]
    [InlineData("k7pq2m94")]
    [InlineData("K7PQ2M9U")]
    public void Decode_RefusesAnythingButACode(string payload) =>
        Assert.Null(NewTeacherInvite.Decode(Encoding.ASCII.GetBytes(payload)));

    [Fact]
    public void AsksForCode_TellsANewAgentsAnswerFromAnOldOne()
    {
        Assert.True(NewTeacherInvite.AsksForCode(NewTeacherInvite.WithCode("liberada até 14:32")));
        Assert.False(NewTeacherInvite.AsksForCode("liberada até 14:32"));
    }

    // The same bytes as the phone's NewTeacherInviteTest.
    [Fact]
    public void KeyAndProof_MatchTheSharedVector()
    {
        var key = NewTeacherInvite.Key("K7PQ2M94", Agent);
        Assert.Equal("35b054ac0ea6fc7720587a0b83f8ede6cbccf432b8b6a27b0a68bc4f72f2c835", Convert.ToHexString(key).ToLowerInvariant());
        Assert.Equal(
            "a70ea9f6f00656859588392e3f9e0ccf72c1e6a6460f471b608c43ba5f84d16e",
            Convert.ToHexString(NewTeacherInvite.Proof(key, Master, Agent, AgentNonce, MasterNonce)).ToLowerInvariant());
    }

    [Fact]
    public void Verifies_TheRightCode() =>
        Assert.True(NewTeacherInvite.Verifies("K7PQ2M94", ProofFor("K7PQ2M94"), Master, Agent, AgentNonce, MasterNonce));

    [Fact]
    public void Verifies_IgnoresTheThumbprintsCase() =>
        Assert.True(NewTeacherInvite.Verifies(
            "K7PQ2M94", ProofFor("K7PQ2M94"), Master.ToLowerInvariant(), " " + Agent.ToLowerInvariant(), AgentNonce, MasterNonce));

    [Fact]
    public void DoesNotVerify_AWrongCode() =>
        Assert.False(NewTeacherInvite.Verifies("K7PQ2M95", ProofFor("K7PQ2M94"), Master, Agent, AgentNonce, MasterNonce));

    // A proof is for one teacher's certificate, one machine and one pairing.
    [Fact]
    public void DoesNotVerify_ForAnotherTeacherMachineOrPairing()
    {
        var proof = ProofFor("K7PQ2M94");
        var other = new string('B', 40);

        Assert.False(NewTeacherInvite.Verifies("K7PQ2M94", proof, other, Agent, AgentNonce, MasterNonce));
        Assert.False(NewTeacherInvite.Verifies("K7PQ2M94", proof, Master, other, AgentNonce, MasterNonce));
        Assert.False(NewTeacherInvite.Verifies("K7PQ2M94", proof, Master, Agent, MasterNonce, AgentNonce));
        Assert.False(NewTeacherInvite.Verifies("K7PQ2M94", proof, Master, Agent, AgentNonce, AgentNonce));
    }

    [Fact]
    public void DoesNotVerify_AProofOfTheWrongSize()
    {
        Assert.False(NewTeacherInvite.Verifies("K7PQ2M94", Array.Empty<byte>(), Master, Agent, AgentNonce, MasterNonce));
        Assert.False(NewTeacherInvite.Verifies("K7PQ2M94", ProofFor("K7PQ2M94")[..31], Master, Agent, AgentNonce, MasterNonce));
    }
}
