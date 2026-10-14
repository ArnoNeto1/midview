using System.Text;
using Midview.Protocol;
using Xunit;

public class TeacherInviteTests
{
    private static readonly string A = new('A', 40);
    private static readonly string B = new('B', 40);

    // The reference values were computed with Python's hashlib, independently
    // of this code.
    [Fact]
    public void Check_MatchesTheReferenceValues()
    {
        Assert.Equal("GCQ63", TeacherInvite.Check(A, "K7PQ"));
        Assert.Equal("6GDSD", TeacherInvite.Check(A, "0000"));
        Assert.Equal("KMTBD", TeacherInvite.Check(B, "K7PQ"));
    }

    [Fact]
    public void Check_DoesNotCareAboutTheThumbprintsCase()
    {
        Assert.Equal("GCQ63", TeacherInvite.Check(A.ToLowerInvariant(), "K7PQ"));
    }

    [Fact]
    public void Check_ChangesWithTheThumbprintAndTheSecret()
    {
        Assert.Equal("KWRSV", TeacherInvite.Check(new string('A', 39) + "C", "K7PQ"));
        Assert.Equal("MC44B", TeacherInvite.Check(A, "K7PR"));
    }

    [Fact]
    public void PointsTo_OnlyTheCertificateTheCodeWasMadeFor()
    {
        Assert.True(TeacherInvite.PointsTo("K7PQGCQ63", A));
        Assert.True(TeacherInvite.PointsTo("K7PQGCQ63", A.ToLowerInvariant()));
        Assert.False(TeacherInvite.PointsTo("K7PQGCQ63", B));
        Assert.False(TeacherInvite.PointsTo("K7PRGCQ63", A));
        Assert.False(TeacherInvite.PointsTo("K7PQGCQ6", A));
    }

    [Fact]
    public void NewCode_IsNineCharactersOfTheAlphabet_AndPointsToItsTeacher()
    {
        for (var i = 0; i < 50; i++)
        {
            var code = TeacherInvite.NewCode(A);

            Assert.Equal(TeacherInvite.CodeLength, code.Length);
            Assert.All(code, c => Assert.Contains(c, TeacherInvite.Alphabet));
            Assert.True(TeacherInvite.PointsTo(code, A));
        }
    }

    [Fact]
    public void NewCode_DrawsTheSecretAtRandom()
    {
        var secrets = Enumerable.Range(0, 20).Select(_ => TeacherInvite.Secret(TeacherInvite.NewCode(A))).Distinct().Count();

        Assert.True(secrets > 1);
    }

    [Theory]
    [InlineData("K7PQ2M94R", "K7PQ2M94R")]
    [InlineData("k7p q2m 94r", "K7PQ2M94R")]
    [InlineData("K7P-Q2M-94R", "K7PQ2M94R")]
    [InlineData("  K7PQ2M94R\t", "K7PQ2M94R")]
    [InlineData("o1l i23 456", "011123456")]
    public void TryRead_TakesTheCodeAsTyped(string typed, string expected)
    {
        Assert.True(TeacherInvite.TryRead(typed, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("K7PQ2M94")]
    [InlineData("K7PQ2M94RX")]
    [InlineData("K7PQ2M94U")]
    [InlineData("K7PQ2M94!")]
    public void TryRead_RefusesWhatIsNotACode(string typed)
    {
        Assert.False(TeacherInvite.TryRead(typed, out var code));
        Assert.Equal(string.Empty, code);
    }

    [Fact]
    public void Format_ShowsThreeGroupsOfThree()
    {
        Assert.Equal("K7P Q2M 94R", TeacherInvite.Format("K7PQ2M94R"));
    }

    [Fact]
    public void SameSecret_ComparesExactly()
    {
        Assert.True(TeacherInvite.SameSecret("K7PQ", "K7PQ"));
        Assert.False(TeacherInvite.SameSecret("K7PR", "K7PQ"));
        Assert.False(TeacherInvite.SameSecret("K7P", "K7PQ"));
    }

    [Theory]
    [InlineData("0123456789abcdefABCDEF0123456789abcdefAB", true)]
    [InlineData("0123456789abcdefABCDEF0123456789abcdefA", false)]
    [InlineData("0123456789abcdefABCDEF0123456789abcdefABC", false)]
    [InlineData("G123456789abcdefABCDEF0123456789abcdefAB", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsThumbprint_IsFortyHexDigits(string? value, bool expected)
    {
        Assert.Equal(expected, TeacherInvite.IsThumbprint(value));
    }

    [Fact]
    public void ProbeAndReply_RoundTrip_AndAreNotMistakenForEachOther()
    {
        var id = Guid.NewGuid();
        var probe = TeacherInvite.EncodeProbe(id);
        var reply = TeacherInvite.EncodeReply(id, "PROF-01", 28902);

        Assert.Equal(id, TeacherInvite.ParseProbe(probe));
        Assert.Equal(new InviteReply(id, "PROF-01", 28902), TeacherInvite.ParseReply(reply));
        Assert.Null(TeacherInvite.ParseReply(probe));
        Assert.Null(TeacherInvite.ParseProbe(reply));
    }

    [Fact]
    public void Reply_CleansTheTeachersName()
    {
        var reply = TeacherInvite.EncodeReply(Guid.NewGuid(), "PROF;01\"&", 28902);

        Assert.Equal("PROF01", TeacherInvite.ParseReply(reply)!.Name);
    }

    // Nothing here is a probe or a reply.
    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"midview\":2,\"invite\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"}")]
    [InlineData("{\"invite\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"}")]
    [InlineData("{\"midview\":1,\"invite\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"midview\":1,\"invite\":\"not a guid\"}")]
    [InlineData("{\"midview\":1,\"probe\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",\"master\":\"AA\"}")]
    [InlineData("{\"midview\":1,\"invite\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",\"name\":\"PROF-01\",\"port\":0}")]
    [InlineData("{\"midview\":1,\"invite\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",\"name\":\"PROF-01\",\"port\":65536}")]
    [InlineData("{\"midview\":1,\"invite\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",\"name\":\"PROF-01\"}")]
    public void Packets_ThatAreNotOurs_AreIgnored(string json)
    {
        var packet = Encoding.UTF8.GetBytes(json);

        Assert.Null(TeacherInvite.ParseReply(packet));
        Assert.Null(TeacherInvite.ParseProbe(packet));
    }

    [Fact]
    public void OversizedPackets_AreIgnored()
    {
        var padding = new string(' ', TeacherInvite.MaxPacketSize);
        var probe = Encoding.UTF8.GetBytes($"{{\"midview\":1,\"invite\":\"{Guid.NewGuid()}\"}}{padding}");

        Assert.Null(TeacherInvite.ParseProbe(probe));
    }

    [Fact]
    public void InviteJoinRequest_RoundTrips()
    {
        var request = new InviteJoinRequest("K7PQ", "PROF-02");

        Assert.Equal(request, InviteJoinRequest.Decode(request.Encode()));
        Assert.Null(InviteJoinRequest.Decode(Encoding.UTF8.GetBytes("not json")));
        Assert.Null(InviteJoinRequest.Decode(Encoding.UTF8.GetBytes("{\"name\":\"PROF-02\"}")));
    }

    [Fact]
    public void TeacherGrant_RoundTrips_UpperCase()
    {
        var grant = new TeacherGrant(B.ToLowerInvariant(), "PROF-02");

        Assert.Equal(new TeacherGrant(B, "PROF-02"), TeacherGrant.Decode(grant.Encode()));
    }

    public static TheoryData<string> BadGrants => new()
    {
        "{\"thumbprint\":\"\",\"name\":\"PROF-02\"}",
        $"{{\"thumbprint\":\"{new string('B', 39)}\",\"name\":\"PROF-02\"}}",
        $"{{\"thumbprint\":\"G{new string('B', 39)}\",\"name\":\"PROF-02\"}}",
        $"{{\"thumbprint\":\"{new string('B', 41)}\",\"name\":\"PROF-02\"}}",
        "{\"name\":\"PROF-02\"}",
        $"{{\"thumbprint\":\"{new string('B', 40)}\"}}",
        "not json",
    };

    [Theory]
    [MemberData(nameof(BadGrants))]
    public void TeacherGrant_WithoutAThumbprint_IsRefused(string json)
    {
        Assert.Null(TeacherGrant.Decode(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void InviteResult_RoundTrips_InOrder()
    {
        var result = new InviteResult(
            new[]
            {
                new InvitedMachine("ALUNO-01", "10.0.0.1", new string('5', 40), A, "00-1A-2B-3C-4D-5E"),
                new InvitedMachine("ALUNO-02", "PC-ALUNO-02", new string('6', 40), B, null),
            },
            new[] { new LeftOutMachine("ALUNO-07", "Offline") });

        var decoded = InviteResult.Decode(result.Encode())!;

        Assert.Equal(result.Given, decoded.Given);
        Assert.Equal(result.LeftOut, decoded.LeftOut);
        Assert.Equal(3, decoded.Total);
    }

    [Fact]
    public void InviteResult_DropsMachinesThatCouldNotBeReached()
    {
        var json = $$"""
            {"given":[
              {"name":"ALUNO-01","address":"10.0.0.1","session":"{{new string('5', 40)}}","agent":"{{A}}","mac":null},
              {"name":"","address":"10.0.0.2","session":"{{new string('5', 40)}}","agent":"{{A}}","mac":null},
              {"name":"ALUNO-03","address":"10.0.0.3","session":"5555","agent":"{{A}}","mac":null},
              {"name":"ALUNO-04","address":"10.0.0.4","session":"{{new string('5', 40)}}","agent":null,"mac":null},
              null
            ],"leftOut":[{"name":"ALUNO-07","reason":"Offline"},null]}
            """;

        var decoded = InviteResult.Decode(Encoding.UTF8.GetBytes(json))!;

        Assert.Equal(new[] { "ALUNO-01" }, decoded.Given.Select(machine => machine.Name));
        Assert.Equal(new[] { new LeftOutMachine("ALUNO-07", "Offline") }, decoded.LeftOut);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"given\":[]}")]
    public void InviteResult_ThatIsNotOne_IsNull(string json)
    {
        Assert.Null(InviteResult.Decode(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Refused_CarriesTheReason()
    {
        var refused = TeacherInvite.Refused("Código errado.");

        Assert.Equal(MessageType.InviteRefused, refused.Type);
        Assert.Equal("Código errado.", TeacherInvite.RefusalText(refused.Payload));
    }
}
