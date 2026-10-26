# Giving a Colleague the Class by Invite Code — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Teacher A, who has a class, shows a 9-character code; teacher B types it and gets every machine of A's class, with no pairing per machine and nothing on the students' screens.

**Architecture:** A's Master listens on TCP and UDP 28902 only while its invite window is open. B broadcasts "who has an invite open?". For each answer, B opens TLS and checks the code's last 5 characters against the certificate it got; only on a match does it send the code's first 4 characters (the secret). A then sends a new trusted-only Agent command, `TrustTeacher`, to every machine in its list, and answers B with the machines that took it. B places them in its classroom list.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), WPF (Master), xUnit, Inno Setup 6. No new packages.

**Spec:** `docs/superpowers/specs/2026-10-14-teacher-invite-design.md`

## Global Constraints

- Build and test with the SDK at `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe` (the `dotnet` on PATH has no SDK). The solution builds with 3 pre-existing `xUnit1031` warnings; nothing else may warn.
- Port: `28902`, TCP and UDP (`TeacherInvite.Port`).
- Code: 9 characters of `0123456789ABCDEFGHJKMNPQRSTVWXYZ`, 4 of secret then 5 of check. Check = first 25 bits of SHA-256(`"Midview convite v1"` ‖ thumbprint in upper-case ASCII hex ‖ secret in ASCII), 5 bits a character, most significant first. Shown as `K7P Q2M 94R`.
- Times: invite lives 10 minutes; B listens 2 s for answers; A waits 30 s for `InviteJoin`; B waits 90 s for the list. B tries at most 10 answers. Before the secret is checked, A reads at most one message of at most 4 KB. UDP packets over 2 KB are ignored.
- `MessageType`: `TrustTeacher = 61`, `InviteJoin = 62`, `InviteDone = 63`, `InviteRefused = 64`.
- Text the teachers read is Brazilian Portuguese, exactly as written in this plan. Code, comments and commit messages are English. Comments explain why, in the style of the surrounding code. Never remove or shorten an existing comment unless the step says so.
- Never name any other classroom-control program anywhere.
- Commit messages: one plain English sentence, no prefix (like `Add a class-wide chat, "Bate-papo da turma"`), then a blank line and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- `src/Midview.Master/OpenWebsiteWindow.xaml.cs` has an unrelated, uncommitted change. Never stage it: `git add` only the files each task names.

## Review Focus

1. **Two teachers with an invite open at the same time:** B's code must reach its own teacher, whichever answers first, and leave the other invite untouched. Test: `WithTwoInvitesOpen_TheCodeFindsItsOwnTeacher` (Task 4).
2. **B already has some of A's machines** (same Agent, or same name): they are replaced in place, not duplicated, and the rest go in classroom order. Test: `AColleaguesClass_PlacedOneByOne_ReplacesWhatWasThere_AndKeepsClassroomOrder` (Task 3).
3. **A code typed loosely**: lower case, spaces, dashes, O for 0, I or L for 1. It must still work, and a U or a wrong length must be refused before anything goes on the network. Test: `TryRead_*` (Task 1).
4. **A's list keeps a machine by computer name, or with lower-case thumbprints**: B gets the name as it is and the thumbprints in upper case. Tests: `Give_SendsThumbprintsUpperCase_AndAddressesAsTheyAre` (Task 3) and `TheMachinesThatCame_GoIntoTheClassroomListAsTheyAre` (Task 4).
5. **Port 28902 already taken** on A's PC: opening the invite fails with a `SocketException` that the window shows, instead of crashing. Test: `APortInUse_CannotOpenASecondInvite` (Task 4).

---

### Task 1: Protocol: the code, the packets, the payloads and the message types

**Files:**
- Create: `src/Midview.Protocol/TeacherInvite.cs`
- Modify: `src/Midview.Protocol/MessageType.cs` (after `ClipboardText = 60,`)
- Modify: `src/Midview.Protocol/MessageFramer.cs` (`Read`)
- Modify: `src/Midview.Protocol/SecureChannel.cs` (after `public Message Receive()`)
- Create: `tests/Midview.Protocol.Tests/TeacherInviteTests.cs`
- Modify: `tests/Midview.Protocol.Tests/MessageFramerTests.cs`

**Interfaces:**
- Consumes: `Pairing.CleanTeacherName(string)`, `MessageFramer`, `SecureChannel` (existing).
- Produces:
  - `static class TeacherInvite`:
    - constants `Port`, `Version`, `MaxPacketSize`, `CodeLength`, `SecretLength`, `MaxJoinLength`, `MaxResponders`, `Alphabet`, `BadCode`;
    - `TimeSpan Lifetime, ListenTime, JoinTimeout, DoneTimeout`;
    - `string NewCode(string thumbprint)`, `string Check(string thumbprint, string secret)`, `string Secret(string code)`;
    - `bool PointsTo(string code, string thumbprint)`, `bool SameSecret(string sent, string expected)`;
    - `bool TryRead(string typed, out string code)`, `string Format(string code)`, `bool IsThumbprint(string? value)`;
    - `byte[] EncodeProbe(Guid id)`, `byte[] EncodeReply(Guid id, string name, int port)`, `Guid? ParseProbe(byte[] packet)`, `InviteReply? ParseReply(byte[] packet)`;
    - `Message Refused(string reason)`, `string RefusalText(byte[] payload)`.
  - Records:
    - `InviteReply(Guid ProbeId, string Name, int Port)`;
    - `InviteJoinRequest(string Secret, string TeacherName)` with `Encode()` / `static Decode(byte[]) → InviteJoinRequest?`;
    - `TeacherGrant(string Thumbprint, string Name)` with `Encode()` / `static Decode(byte[]) → TeacherGrant?`;
    - `InvitedMachine(string Name, string HostAddress, string SessionThumbprint, string AgentThumbprint, string? MacAddress)`;
    - `LeftOutMachine(string Name, string Reason)`;
    - `InviteResult(IReadOnlyList<InvitedMachine> Given, IReadOnlyList<LeftOutMachine> LeftOut)` with `int Total`, `static InviteResult Empty`, `Encode()`, `static Decode(byte[]) → InviteResult?`.
  - `MessageFramer.Read(Stream stream, int maxLength)` and `SecureChannel.Receive(int maxLength)`.
  - `MessageType.TrustTeacher`, `InviteJoin`, `InviteDone`, `InviteRefused`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Midview.Protocol.Tests/TeacherInviteTests.cs`:

```csharp
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
```

Add to `tests/Midview.Protocol.Tests/MessageFramerTests.cs`, inside the class, after `WriteThenRead_RoundTripsMessage`:

```csharp
    [Fact]
    public void Read_WithALimit_RefusesALongerMessage_BeforeReadingIt()
    {
        var stream = new MemoryStream();
        MessageFramer.Write(stream, new Message(MessageType.InviteJoin, new byte[100]));

        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => MessageFramer.Read(stream, 100));

        stream.Position = 0;
        Assert.Equal(100, MessageFramer.Read(stream, 101).Payload.Length);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Protocol.Tests --filter "FullyQualifiedName~TeacherInviteTests|FullyQualifiedName~MessageFramerTests"`
Expected: build FAILS with `CS0103: The name 'TeacherInvite' does not exist` (and `MessageType.InviteJoin` not found).

- [ ] **Step 3: Add the message types**

In `src/Midview.Protocol/MessageType.cs`, after `ClipboardText = 60,` and before the enum's closing `}`:

```csharp

    // "Dar acesso a um colega" (see TeacherInvite and
    // docs/superpowers/specs/2026-10-14-teacher-invite-design.md).
    // TrustTeacher is an Agent command, from a trusted teacher only: trust
    // another teacher too (a TeacherGrant), with nothing on the student's
    // screen. Answered with one CommandAck carrying a CommandResult; an Agent
    // from before this answers CommandAck [0], which the Master shows as
    // "Midview antigo".
    TrustTeacher = 61,

    // Teacher to teacher, on TeacherInvite.Port, never to a student machine:
    // the colleague's InviteJoin (an InviteJoinRequest) is answered with
    // InviteDone (an InviteResult) or InviteRefused (why, UTF-8, in
    // Portuguese).
    InviteJoin = 62,
    InviteDone = 63,
    InviteRefused = 64,
```

- [ ] **Step 4: Add the read limit**

In `src/Midview.Protocol/MessageFramer.cs`, replace the start of `Read`:

```csharp
    public static Message Read(Stream stream)
    {
        var header = ReadExact(stream, 4);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 1 || length > MaxMessageLength)
        {
            throw new InvalidDataException($"Invalid message length {length}");
        }
```

with:

```csharp
    public static Message Read(Stream stream) => Read(stream, MaxMessageLength);

    // For a side that doesn't trust the other yet (see TeacherInvite): a
    // message whose type and payload are longer than maxLength is refused
    // before it is read.
    public static Message Read(Stream stream, int maxLength)
    {
        var header = ReadExact(stream, 4);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 1 || length > Math.Min(maxLength, MaxMessageLength))
        {
            throw new InvalidDataException($"Invalid message length {length}");
        }
```

(The rest of `Read` stays as it is.)

In `src/Midview.Protocol/SecureChannel.cs`, after `public Message Receive() => MessageFramer.Read(_sslStream);`:

```csharp

    public Message Receive(int maxLength) => MessageFramer.Read(_sslStream, maxLength);
```

- [ ] **Step 5: Write `TeacherInvite.cs`**

Create `src/Midview.Protocol/TeacherInvite.cs`:

```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midview.Protocol;

// "Dar acesso a um colega" (docs/superpowers/specs/2026-10-14-teacher-invite-design.md):
// teacher A reads out a code, teacher B types it, and B gets A's class.
//   B -> broadcast, UDP Port  who has an invite open? (EncodeProbe)
//   A -> B                    its computer name and TCP port (EncodeReply);
//                             nothing from the code
//   B -> A, TLS on that port  InviteJoin (InviteJoinRequest): the code's
//                             secret and B's computer name, sent only once
//                             the code's check matched A's certificate
//   A -> B                    InviteDone (InviteResult), or InviteRefused
//                             (why, in Portuguese)
// The code is a secret and a check made from A's certificate and that
// secret. B never sends the secret where the check fails, so a computer in
// the middle never hears it; someone guessing at A has one chance in about
// a million, and A ends the invite at the first wrong guess.
public static class TeacherInvite
{
    public const int Port = 28902;
    public const int Version = 1;
    public const int MaxPacketSize = 2048;
    public const int CodeLength = 9;
    public const int SecretLength = 4;

    // Before the secret is checked, A reads one message of at most this.
    public const int MaxJoinLength = 4096;

    // How many of the teachers that answered B tries.
    public const int MaxResponders = 10;

    // Crockford's Base32: no I, L, O or U, so nothing read out is mistaken
    // for something else (TryRead takes O for 0, and I or L for 1).
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const string BadCode = "O código tem 9 letras e números, como K7P Q2M 94R.";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // How long B listens for answers (as Discovery.ListenTime).
    public static readonly TimeSpan ListenTime = TimeSpan.FromSeconds(2);

    // How long A waits for InviteJoin on a connection.
    public static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(30);

    // How long B waits for the list once it sent the secret: A tells every
    // machine of its class first, in parallel, each within CommandClient's
    // timeouts.
    public static readonly TimeSpan DoneTimeout = TimeSpan.FromSeconds(90);

    private static readonly byte[] Label = Encoding.ASCII.GetBytes("Midview convite v1");

    // A new code for the teacher whose certificate has this thumbprint.
    public static string NewCode(string thumbprint)
    {
        var secret = new string(Enumerable.Range(0, SecretLength)
            .Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)])
            .ToArray());
        return secret + Check(thumbprint, secret);
    }

    // The code's last 5 characters: the first 25 bits of
    // SHA-256("Midview convite v1" || thumbprint || secret), 5 bits a
    // character, most significant first. Thumbprint: hex, upper case, ASCII.
    public static string Check(string thumbprint, string secret)
    {
        var input = Label
            .Concat(Encoding.ASCII.GetBytes(thumbprint.Trim().ToUpperInvariant()))
            .Concat(Encoding.ASCII.GetBytes(secret))
            .ToArray();
        var bits = BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(input)) >> 7;
        var check = new char[CodeLength - SecretLength];
        for (var i = 0; i < check.Length; i++)
        {
            check[i] = Alphabet[(int)((bits >> (5 * (check.Length - 1 - i))) & 31)];
        }
        return new string(check);
    }

    public static string Secret(string code) => code[..SecretLength];

    // Whether the code was made for the certificate with this thumbprint:
    // how B tells A's program from anyone else's.
    public static bool PointsTo(string code, string thumbprint) =>
        code.Length == CodeLength
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Check(thumbprint, Secret(code))), Encoding.ASCII.GetBytes(code[SecretLength..]));

    public static bool SameSecret(string sent, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(expected));

    // What the teacher typed, as a code: case doesn't matter, spaces and
    // dashes are dropped, O is read as 0 and I or L as 1. False (and code
    // empty) unless that leaves 9 characters of the alphabet.
    public static bool TryRead(string typed, out string code)
    {
        var cleaned = new string(typed
            .Where(c => !char.IsWhiteSpace(c) && c != '-')
            .Select(c => char.ToUpperInvariant(c) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other,
            })
            .ToArray());
        var valid = cleaned.Length == CodeLength && cleaned.All(c => Alphabet.Contains(c));
        code = valid ? cleaned : string.Empty;
        return valid;
    }

    // As A's window shows it: "K7P Q2M 94R".
    public static string Format(string code) => $"{code[..3]} {code[3..6]} {code[6..]}";

    // A certificate's thumbprint as Windows gives it: SHA-1, 40 hex digits.
    public static bool IsThumbprint(string? value) => value is { Length: 40 } && value.All(Uri.IsHexDigit);

    public static byte[] EncodeProbe(Guid id) =>
        JsonSerializer.SerializeToUtf8Bytes(new PacketJson(Version, id, null, null));

    public static byte[] EncodeReply(Guid id, string name, int port) =>
        JsonSerializer.SerializeToUtf8Bytes(new PacketJson(Version, id, name, port));

    // A probe has no name and no port; a reply has both.
    public static Guid? ParseProbe(byte[] packet) =>
        Read(packet) is { Midview: Version, Name: null, Port: null } json && json.Invite != Guid.Empty
            ? json.Invite
            : null;

    public static InviteReply? ParseReply(byte[] packet) =>
        Read(packet) is { Midview: Version, Name: { Length: > 0 } name, Port: >= 1 and <= 65535 } json && json.Invite != Guid.Empty
            ? new InviteReply(json.Invite, Pairing.CleanTeacherName(name), json.Port.Value)
            : null;

    public static Message Refused(string reason) => new(MessageType.InviteRefused, Encoding.UTF8.GetBytes(reason));

    public static string RefusalText(byte[] payload) => Encoding.UTF8.GetString(payload);

    private static PacketJson? Read(byte[] packet)
    {
        if (packet.Length == 0 || packet.Length > MaxPacketSize)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PacketJson>(packet);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record PacketJson(
        [property: JsonPropertyName("midview")] int Midview,
        [property: JsonPropertyName("invite")] Guid Invite,
        [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
        [property: JsonPropertyName("port"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Port);
}

// A teacher with an invite open: its computer name, and where to connect
// (the answer's source address, on Port).
public sealed record InviteReply(Guid ProbeId, string Name, int Port);

// InviteJoin's payload.
public sealed record InviteJoinRequest(
    [property: JsonPropertyName("secret")] string Secret,
    [property: JsonPropertyName("name")] string TeacherName)
{
    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static InviteJoinRequest? Decode(byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize<InviteJoinRequest>(payload) is { Secret: not null, TeacherName: not null } request
                ? request
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// TrustTeacher's payload: the teacher to trust, and their computer name for
// the Agent's log.
public sealed record TeacherGrant(
    [property: JsonPropertyName("thumbprint")] string Thumbprint,
    [property: JsonPropertyName("name")] string Name)
{
    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this);

    // Null unless the thumbprint is one (TeacherInvite.IsThumbprint); the
    // thumbprint comes back upper case and the name cleaned.
    public static TeacherGrant? Decode(byte[] payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TeacherGrant>(payload) is { Name: not null } grant
                   && TeacherInvite.IsThumbprint(grant.Thumbprint)
                ? new TeacherGrant(grant.Thumbprint.ToUpperInvariant(), Pairing.CleanTeacherName(grant.Name))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// One machine as InviteDone carries it: as A's classroom list has it.
public sealed record InvitedMachine(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string HostAddress,
    [property: JsonPropertyName("session")] string SessionThumbprint,
    [property: JsonPropertyName("agent")] string AgentThumbprint,
    [property: JsonPropertyName("mac")] string? MacAddress);

// A machine of A's class the colleague didn't get, and why (in Portuguese).
public sealed record LeftOutMachine(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("reason")] string Reason);

// InviteDone's payload: the machines that now trust the colleague, in A's
// classroom order, and the ones that don't.
public sealed record InviteResult(
    [property: JsonPropertyName("given")] IReadOnlyList<InvitedMachine> Given,
    [property: JsonPropertyName("leftOut")] IReadOnlyList<LeftOutMachine> LeftOut)
{
    public static InviteResult Empty { get; } = new(Array.Empty<InvitedMachine>(), Array.Empty<LeftOutMachine>());

    [JsonIgnore]
    public int Total => Given.Count + LeftOut.Count;

    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this);

    // Null if it isn't one. A machine missing its name or address, or with a
    // thumbprint that isn't one, is dropped: B could never reach it.
    public static InviteResult? Decode(byte[] payload)
    {
        try
        {
            if (JsonSerializer.Deserialize<InviteResult>(payload) is not { Given: not null, LeftOut: not null } result)
            {
                return null;
            }

            return new InviteResult(
                result.Given
                    .Where(machine => machine is { Name.Length: > 0, HostAddress.Length: > 0 }
                        && TeacherInvite.IsThumbprint(machine.SessionThumbprint)
                        && TeacherInvite.IsThumbprint(machine.AgentThumbprint))
                    .ToList(),
                result.LeftOut.Where(machine => machine is { Name: not null, Reason: not null }).ToList());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Protocol.Tests`
Expected: PASS, every test (the old ones too).

- [ ] **Step 7: Commit**

```bash
git add src/Midview.Protocol/TeacherInvite.cs src/Midview.Protocol/MessageType.cs src/Midview.Protocol/MessageFramer.cs src/Midview.Protocol/SecureChannel.cs tests/Midview.Protocol.Tests/TeacherInviteTests.cs tests/Midview.Protocol.Tests/MessageFramerTests.cs
git commit -F - <<'EOF'
Add the invite code and messages for giving a colleague the class

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Agent: `TrustTeacher`

**Files:**
- Modify: `src/Midview.Agent/PairingService.cs`
- Modify: `src/Midview.Agent/AgentServer.cs` (after the `AllowNewTeacher` block)
- Modify: `tests/Midview.Agent.Tests/PairingServiceTests.cs`
- Modify: `tests/Midview.Agent.Tests/AgentPairingTests.cs`

**Interfaces:**
- Consumes: `TeacherGrant.Decode(byte[])`, `MessageType.TrustTeacher`, `TrustedMastersFile.Add` (Task 1, existing).
- Produces: `PairingService.TrustTeacher(string senderThumbprint, byte[] payload) → (bool Succeeded, string Detail)`. It returns `(true, "liberada")`, `(false, "identificação de professor inválida")` or `(false, "não foi possível gravar o professor nesta máquina: …")`. The Agent answers `TrustTeacher` from a trusted teacher with `CommandAck` + `CommandResult`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Midview.Agent.Tests/PairingServiceTests.cs`, inside the class (it already has `TeacherA`, `TeacherB`, `_path`, `_display`, `NewService()` and `Trust(...)`):

```csharp
    [Fact]
    public void TrustTeacher_AddsTheColleague_AndKeepsTheOthers()
    {
        Trust(TeacherA);

        var (succeeded, detail) = NewService().TrustTeacher(TeacherA, new TeacherGrant(TeacherB.ToLowerInvariant(), "PROF-02").Encode());

        Assert.True(succeeded);
        Assert.Equal("liberada", detail);
        Assert.Equal(new[] { TeacherA, TeacherB }, TrustedMastersFile.Load(_path));
        Assert.Empty(_display.Shown);
    }

    [Fact]
    public void TrustTeacher_Twice_KeepsOneEntry()
    {
        Trust(TeacherA);
        var service = NewService();
        var grant = new TeacherGrant(TeacherB, "PROF-02").Encode();

        service.TrustTeacher(TeacherA, grant);
        service.TrustTeacher(TeacherA, grant);

        Assert.Equal(new[] { TeacherA, TeacherB }, TrustedMastersFile.Load(_path));
    }

    public static TheoryData<string> BadGrants => new()
    {
        "{\"thumbprint\":\"\",\"name\":\"PROF-02\"}",
        $"{{\"thumbprint\":\"{new string('B', 39)}\",\"name\":\"PROF-02\"}}",
        $"{{\"thumbprint\":\"G{new string('B', 39)}\",\"name\":\"PROF-02\"}}",
        "{\"name\":\"PROF-02\"}",
        "not json",
    };

    [Theory]
    [MemberData(nameof(BadGrants))]
    public void TrustTeacher_WithoutAThumbprint_IsRefused(string payload)
    {
        Trust(TeacherA);

        var (succeeded, detail) = NewService().TrustTeacher(TeacherA, Encoding.UTF8.GetBytes(payload));

        Assert.False(succeeded);
        Assert.Equal("identificação de professor inválida", detail);
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    [Fact]
    public void TrustTeacher_WithAnUnreadableList_IsRefused_AndLeavesItAlone()
    {
        File.WriteAllText(_path, "not json");

        var (succeeded, detail) = NewService().TrustTeacher(TeacherA, new TeacherGrant(TeacherB, "PROF-02").Encode());

        Assert.False(succeeded);
        Assert.StartsWith("não foi possível gravar o professor nesta máquina", detail);
        Assert.Equal("not json", File.ReadAllText(_path));
    }
```

In `tests/Midview.Agent.Tests/AgentPairingTests.cs`:

1. Replace `Connect()` and `Command(MessageType type)` with versions that take the teacher and the payload:

```csharp
    private (TcpClient Tcp, SecureChannel Channel) Connect(X509Certificate2? teacher = null)
    {
        var tcp = new TcpClient();
        tcp.Connect(IPAddress.Loopback, _port);
        tcp.ReceiveTimeout = 30_000;
        return (tcp, SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, "agent-under-test", teacher ?? _teacher));
    }
```

```csharp
    // The reply, or null when the Agent closed the connection unanswered.
    private Message? Command(MessageType type, byte[]? payload = null, X509Certificate2? teacher = null)
    {
        var (tcp, channel) = Connect(teacher);
        using var _ = tcp;
        using var __ = channel;
        channel.Send(new Message(type, payload ?? Array.Empty<byte>()));
        try
        {
            return channel.Receive();
        }
        catch (Exception)
        {
            return null;
        }
    }
```

(`PairOverTheNetwork` calls `Connect()` with no argument and keeps working.)

2. Add `[InlineData(MessageType.TrustTeacher)]` to `ATeacherItDoesNotTrust_IsCutOffUnanswered`, after `[InlineData(MessageType.AllowNewTeacher)]`.

3. Add the test:

```csharp
    [Fact]
    public async Task TrustTeacher_FromATrustedTeacher_LetsTheColleagueIn()
    {
        using var colleague = SelfSignedCertificate.Create("colleague-under-test");
        File.WriteAllText(_trustedPath, $"[\"{_teacher.Thumbprint}\"]");
        Assert.Null(await Task.Run(() => Command(MessageType.UnblockInternet, teacher: colleague)).WaitAsync(TimeSpan.FromSeconds(30)));

        var ack = await Task.Run(() => Command(MessageType.TrustTeacher, new TeacherGrant(colleague.Thumbprint, "PROF-02").Encode()))
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal((true, "liberada"), CommandResult.Decode(ack!.Payload));
        Assert.Empty(_display.Shown);
        var reply = await Task.Run(() => Command(MessageType.UnblockInternet, teacher: colleague)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(MessageType.CommandAck, reply!.Type);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Agent.Tests --filter "FullyQualifiedName~PairingServiceTests|FullyQualifiedName~AgentPairingTests"`
Expected: build FAILS with `CS1061: 'PairingService' does not contain a definition for 'TrustTeacher'`.

- [ ] **Step 3: Implement `TrustTeacher` in `PairingService`**

In `src/Midview.Agent/PairingService.cs`:

1. Next to `private readonly object _gate = new();`, add:

```csharp
    // trusted-masters.json is read, changed and written back: two teachers
    // added at once (a pairing and a TrustTeacher) must not lose one.
    private readonly object _writeGate = new();
```

2. In `Exchange`, replace:

```csharp
            TrustedMastersFile.Add(_trustedMastersPath, masterThumbprint);
```

with:

```csharp
            lock (_writeGate)
            {
                TrustedMastersFile.Add(_trustedMastersPath, masterThumbprint);
            }
```

3. After the `AllowNewTeacher()` method, add:

```csharp
    // TrustTeacher, from a trusted teacher (AgentServer checks that): trust
    // the colleague that teacher let in with "Dar acesso a um colega".
    // Nothing shows on the student's screen: the colleague was checked by
    // the teacher's own program, with the code.
    public (bool Succeeded, string Detail) TrustTeacher(string senderThumbprint, byte[] payload)
    {
        if (TeacherGrant.Decode(payload) is not { } grant)
        {
            return (false, "identificação de professor inválida");
        }

        try
        {
            lock (_writeGate)
            {
                TrustedMastersFile.Add(_trustedMastersPath, grant.Thumbprint);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (false, $"não foi possível gravar o professor nesta máquina: {ErrorText.Describe(ex)}");
        }

        AgentLog.Warn($"Teacher {senderThumbprint} gave access to teacher {grant.Thumbprint} ({grant.Name}).");
        return (true, "liberada");
    }
```

- [ ] **Step 4: Dispatch it in `AgentServer`**

In `src/Midview.Agent/AgentServer.cs`, right after the `if (message.Type == MessageType.AllowNewTeacher) { … }` block:

```csharp

        if (message.Type == MessageType.TrustTeacher)
        {
            var (trusted, trustDetail) = _pairing?.TrustTeacher(channel.RemoteThumbprint, message.Payload)
                ?? (false, "esta máquina não aceita outro professor");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(trusted, trustDetail)));
            return;
        }
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Agent.Tests`
Expected: PASS (the tests that need administrator rights are skipped as before).

- [ ] **Step 6: Commit**

```bash
git add src/Midview.Agent/PairingService.cs src/Midview.Agent/AgentServer.cs tests/Midview.Agent.Tests/PairingServiceTests.cs tests/Midview.Agent.Tests/AgentPairingTests.cs
git commit -F - <<'EOF'
Let a trusted teacher make a machine trust a colleague

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Master: telling the machines, and what the windows say

**Files:**
- Create: `src/Midview.Master/ClassGrant.cs`
- Create: `src/Midview.Master/InviteText.cs`
- Create: `tests/Midview.Master.Tests/ClassGrantTests.cs`
- Create: `tests/Midview.Master.Tests/InviteTextTests.cs`
- Modify: `tests/Midview.Master.Tests/RosterTests.cs`

**Interfaces:**
- Consumes: `TeacherGrant`, `InviteResult`, `InvitedMachine`, `LeftOutMachine`, `TeacherInvite.IsThumbprint`, `MessageType.TrustTeacher` (Task 1); `CommandClient.SendCommandForResult`, `ConnectionClient.AgentPort`, `FailureText.DescribeAgent`, `KnownMachine`, `Roster.Place` (existing).
- Produces:
  - `ClassGrant.Give(IReadOnlyList<KnownMachine> roster, Func<KnownMachine, (bool Succeeded, string Detail)> tell) → InviteResult`
  - `ClassGrant.OverTheNetwork(X509Certificate2 masterCertificate, TeacherGrant grant) → Func<KnownMachine, (bool Succeeded, string Detail)>`
  - constants `ClassGrant.NoAgentIdentity`, `ClassGrant.OldAgent`
  - `InviteText.Waiting(int machines, DateTime expiresAt)`, `InviteText.Given(string teacherName, InviteResult result)`, `InviteText.Received(InviteResult result)`, each returning `string`

- [ ] **Step 1: Write the failing tests**

Create `tests/Midview.Master.Tests/ClassGrantTests.cs`:

```csharp
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class ClassGrantTests
{
    private static readonly string Session = new('5', 40);

    private static KnownMachine Machine(int number, char agent, string? mac = null) =>
        new($"Aluno-{number}", $"10.0.0.{number}", Session, new string(agent, 40), mac);

    [Fact]
    public void Give_ListsWhoTookIt_InClassroomOrder_AndWhyTheOthersDidNot()
    {
        var offline = new SocketException((int)SocketError.TimedOut);
        var roster = new[]
        {
            Machine(1, 'A', "00-1A-2B-3C-4D-5E"),
            Machine(2, 'B'),
            Machine(3, 'C'),
            Machine(4, 'D'),
            new KnownMachine("Aluno-5", "10.0.0.5", Session, null),
            Machine(6, 'E'),
        };
        var told = new List<string>();

        var result = ClassGrant.Give(roster, machine =>
        {
            lock (told)
            {
                told.Add(machine.Name);
            }
            return machine.Name switch
            {
                "Aluno-2" => (false, string.Empty),
                "Aluno-3" => throw offline,
                "Aluno-4" => (false, "não foi possível gravar o professor nesta máquina: disco cheio"),
                _ => (true, "liberada"),
            };
        });

        Assert.Equal(new[]
        {
            new InvitedMachine("Aluno-1", "10.0.0.1", Session, new string('A', 40), "00-1A-2B-3C-4D-5E"),
            new InvitedMachine("Aluno-6", "10.0.0.6", Session, new string('E', 40), null),
        }, result.Given);
        Assert.Equal(new[]
        {
            new LeftOutMachine("Aluno-2", ClassGrant.OldAgent),
            new LeftOutMachine("Aluno-3", FailureText.DescribeAgent(offline)),
            new LeftOutMachine("Aluno-4", "não foi possível gravar o professor nesta máquina: disco cheio"),
            new LeftOutMachine("Aluno-5", ClassGrant.NoAgentIdentity),
        }, result.LeftOut);
        Assert.DoesNotContain("Aluno-5", told);
    }

    [Fact]
    public void Give_SendsThumbprintsUpperCase_AndAddressesAsTheyAre()
    {
        var roster = new[] { new KnownMachine("Aluno-1", "PC-ALUNO-1", new string('e', 40), new string('a', 40)) };

        var result = ClassGrant.Give(roster, _ => (true, "liberada"));

        Assert.Equal(new InvitedMachine("Aluno-1", "PC-ALUNO-1", new string('E', 40), new string('A', 40), null), result.Given.Single());
    }

    [Fact]
    public void Give_LeavesOutAMachineWhoseThumbprintsAreNotOnes()
    {
        var roster = new[] { new KnownMachine("Aluno-1", "10.0.0.1", "not a thumbprint", new string('A', 40)) };

        var result = ClassGrant.Give(roster, _ => throw new InvalidOperationException("must not be told"));

        Assert.Equal(new[] { new LeftOutMachine("Aluno-1", ClassGrant.NoAgentIdentity) }, result.LeftOut);
    }

    [Fact]
    public void Give_WithAnEmptyList_GivesNothing()
    {
        Assert.Equal(0, ClassGrant.Give(Array.Empty<KnownMachine>(), _ => (true, "liberada")).Total);
    }
}
```

Create `tests/Midview.Master.Tests/InviteTextTests.cs`:

```csharp
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class InviteTextTests
{
    private static readonly string Thumbprint = new('A', 40);

    private static InvitedMachine Machine(string name) => new(name, "10.0.0.1", Thumbprint, Thumbprint, null);

    private static readonly InviteResult SomeLeftOut = new(
        new[] { Machine("ALUNO-01"), Machine("ALUNO-02") },
        new[] { new LeftOutMachine("ALUNO-07", "Offline"), new LeftOutMachine("ALUNO-12", "Midview antigo: atualize-a") });

    [Fact]
    public void Received_SaysHowManyCame_AndWhyTheOthersDidNot()
    {
        Assert.Equal(
            "Pronto: 2 de 4 máquinas na sua sala. Não liberadas: ALUNO-07 (Offline); ALUNO-12 (Midview antigo: atualize-a).",
            InviteText.Received(SomeLeftOut));
        Assert.Equal(
            "Pronto: 1 de 1 máquinas na sua sala.",
            InviteText.Received(new InviteResult(new[] { Machine("ALUNO-01") }, Array.Empty<LeftOutMachine>())));
        Assert.Equal("A sala do colega não tem máquinas.", InviteText.Received(InviteResult.Empty));
    }

    [Fact]
    public void Given_SaysWhoGotTheClass()
    {
        Assert.Equal(
            "PROF-02 recebeu acesso a 2 de 4 máquinas. Não liberadas: ALUNO-07 (Offline); ALUNO-12 (Midview antigo: atualize-a).",
            InviteText.Given("PROF-02", SomeLeftOut));
        Assert.Equal("PROF-02 entrou, mas a sua lista da sala está vazia.", InviteText.Given("PROF-02", InviteResult.Empty));
    }

    [Fact]
    public void Waiting_SaysUntilWhen_AndHowManyMachines()
    {
        var at = new DateTime(2026, 10, 14, 14, 32, 0);

        Assert.Equal(
            "Vale por 10 minutos, uma vez (até 14:32). Deixe esta janela aberta até o colega entrar. Vai liberar as 25 máquinas da sua sala.",
            InviteText.Waiting(25, at));
        Assert.Equal(
            "Vale por 10 minutos, uma vez (até 14:32). Deixe esta janela aberta até o colega entrar. Vai liberar a máquina da sua sala.",
            InviteText.Waiting(1, at));
    }
}
```

Add to `tests/Midview.Master.Tests/RosterTests.cs`, inside the class (it has `X`, `Y`, `Machine(name, host, agent, mac)`):

```csharp
    // How MainWindow takes a colleague's class: Place, one machine at a time,
    // in the colleague's order.
    [Fact]
    public void AColleaguesClass_PlacedOneByOne_ReplacesWhatWasThere_AndKeepsClassroomOrder()
    {
        var roster = new List<KnownMachine> { Machine("Aluno-2", "10.0.0.2", X), Machine("Lab-9", "10.0.0.9", null) };
        var given = new[]
        {
            Machine("Aluno-1", "10.0.0.1", new string('C', 40)),
            Machine("Aluno-2", "10.0.0.22", X),
            Machine("Aluno-10", "10.0.0.10", Y),
        };

        foreach (var machine in given)
        {
            var (index, replaces, placed) = Roster.Place(roster, machine);
            if (replaces)
            {
                roster.RemoveAt(index);
            }
            roster.Insert(index, placed);
        }

        Assert.Equal(new[] { "Aluno-1", "Aluno-2", "Aluno-10", "Lab-9" }, roster.Select(machine => machine.Name));
        Assert.Equal("10.0.0.22", roster[1].HostAddress);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Master.Tests --filter "FullyQualifiedName~ClassGrantTests|FullyQualifiedName~InviteTextTests|FullyQualifiedName~RosterTests"`
Expected: build FAILS with `CS0103: The name 'ClassGrant' does not exist`.

- [ ] **Step 3: Write `ClassGrant.cs`**

Create `src/Midview.Master/ClassGrant.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Dar acesso a um colega", on the machines: TrustTeacher to every machine
// in the classroom list at once (as "Permitir novo professor" does), and
// what the colleague gets back: the machines that took it, as this list has
// them, and why the others didn't.
public static class ClassGrant
{
    public const string NoAgentIdentity = "sem a identificação do Agent: pareie-a de novo";
    public const string OldAgent = "Midview antigo: atualize-a";

    // tell sends TrustTeacher to one machine (OverTheNetwork; a fake in
    // tests). Blocks until every machine answered or failed.
    public static InviteResult Give(IReadOnlyList<KnownMachine> roster, Func<KnownMachine, (bool Succeeded, string Detail)> tell)
    {
        var answers = Task.WhenAll(roster.Select(machine => Task.Run(() => Answer(machine, tell)))).GetAwaiter().GetResult();
        var given = new List<InvitedMachine>();
        var leftOut = new List<LeftOutMachine>();
        for (var i = 0; i < roster.Count; i++)
        {
            var machine = roster[i];
            var (succeeded, detail) = answers[i];
            if (succeeded)
            {
                given.Add(new InvitedMachine(
                    machine.Name, machine.HostAddress, machine.SessionThumbprint.Trim().ToUpperInvariant(),
                    machine.AgentThumbprint!.Trim().ToUpperInvariant(), machine.MacAddress));
            }
            else
            {
                leftOut.Add(new LeftOutMachine(machine.Name, detail));
            }
        }
        return new InviteResult(given, leftOut);
    }

    // TrustTeacher over the network, pinned to each machine's Agent.
    public static Func<KnownMachine, (bool Succeeded, string Detail)> OverTheNetwork(X509Certificate2 masterCertificate, TeacherGrant grant)
    {
        var payload = grant.Encode();
        return machine => CommandClient.SendCommandForResult(
            machine.HostAddress, ConnectionClient.AgentPort, machine.AgentThumbprint!, masterCertificate,
            MessageType.TrustTeacher, payload);
    }

    // Only a machine the colleague could use is told: both thumbprints there
    // to pin. An Agent from before TrustTeacher answers with an empty
    // CommandResult.
    private static (bool Succeeded, string Detail) Answer(KnownMachine machine, Func<KnownMachine, (bool Succeeded, string Detail)> tell)
    {
        if (!TeacherInvite.IsThumbprint(machine.AgentThumbprint?.Trim()) || !TeacherInvite.IsThumbprint(machine.SessionThumbprint.Trim()))
        {
            return (false, NoAgentIdentity);
        }

        try
        {
            var (succeeded, detail) = tell(machine);
            return (succeeded, succeeded || detail.Length > 0 ? detail : OldAgent);
        }
        catch (Exception ex)
        {
            return (false, FailureText.DescribeAgent(ex));
        }
    }
}
```

- [ ] **Step 4: Write `InviteText.cs`**

Create `src/Midview.Master/InviteText.cs`:

```csharp
using Midview.Protocol;

namespace Midview.Master;

// What the two "Dar acesso a um colega" windows say.
public static class InviteText
{
    public static string Waiting(int machines, DateTime expiresAt) =>
        $"Vale por 10 minutos, uma vez (até {expiresAt:HH:mm}). Deixe esta janela aberta até o colega entrar. "
        + (machines == 1 ? "Vai liberar a máquina da sua sala." : $"Vai liberar as {machines} máquinas da sua sala.");

    // The teacher who gave the class: who got it.
    public static string Given(string teacherName, InviteResult result) =>
        result.Total == 0
            ? $"{teacherName} entrou, mas a sua lista da sala está vazia."
            : $"{teacherName} recebeu acesso a {result.Given.Count} de {result.Total} máquinas.{LeftOut(result)}";

    // The colleague: what came in.
    public static string Received(InviteResult result) =>
        result.Total == 0
            ? "A sala do colega não tem máquinas."
            : $"Pronto: {result.Given.Count} de {result.Total} máquinas na sua sala.{LeftOut(result)}";

    private static string LeftOut(InviteResult result) =>
        result.LeftOut.Count == 0
            ? string.Empty
            : $" Não liberadas: {string.Join("; ", result.LeftOut.Select(machine => $"{machine.Name} ({machine.Reason})"))}.";
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Master.Tests --filter "FullyQualifiedName~ClassGrantTests|FullyQualifiedName~InviteTextTests|FullyQualifiedName~RosterTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Midview.Master/ClassGrant.cs src/Midview.Master/InviteText.cs tests/Midview.Master.Tests/ClassGrantTests.cs tests/Midview.Master.Tests/InviteTextTests.cs tests/Midview.Master.Tests/RosterTests.cs
git commit -F - <<'EOF'
Tell the whole class to trust a colleague, and say how it went

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: Master: the invite, both sides (`InviteHost`, `InviteJoin`)

**Files:**
- Create: `src/Midview.Master/InviteHost.cs`
- Create: `src/Midview.Master/InviteJoin.cs`
- Modify: `src/Midview.Master/DiscoveryClient.cs` (`SioUdpConnReset` becomes `internal`)
- Modify: `src/Midview.Master/InviteText.cs` (add `Ended`)
- Create: `tests/Midview.Master.Tests/InviteTests.cs`
- Modify: `tests/Midview.Master.Tests/InviteTextTests.cs`

**Interfaces:**
- Consumes: everything from Task 1; `InviteText` (Task 3); `TcpConnect.WithTimeout`, `FailureText.DescribeAgent`, `WakeOnLan.NormalizeMacAddress`, `KnownMachine`, `SecureChannel` (existing).
- Produces:
  - `enum InviteEnd { Given, WrongCode, Expired, Cancelled }`
  - `record InviteOutcome(InviteEnd End, string? TeacherName = null, InviteResult? Result = null, bool ListSent = false)`
  - `sealed class InviteHost : IDisposable`:
    - `static Open(X509Certificate2 certificate, string teacherName, Func<TeacherGrant, InviteResult> giveClass, TimeSpan lifetime, Action<string>? joining = null, int tcpPort = TeacherInvite.Port, int udpPort = TeacherInvite.Port)`, which throws `SocketException`;
    - `string Code`, `int TcpPort`, `int UdpPort`, `Task<InviteOutcome> Outcome`, `Dispose()`;
    - constants `WrongCodeText`, `GoneText`, and `static string SameIdentityText(string teacherName)`.
  - `sealed class InviteJoinException(string message) : Exception`
  - `static class InviteJoin`:
    - `Join(string code, X509Certificate2 certificate, string teacherName, IReadOnlyList<IPEndPoint> probeTargets, TimeSpan listenTime, Action<string>? progress = null) → (string From, InviteResult Result)`;
    - `Find(IReadOnlyList<IPEndPoint> targets, TimeSpan listenTime) → IReadOnlyList<(IPEndPoint Endpoint, string Name)>`;
    - `ToKnownMachines(InviteResult result) → IReadOnlyList<KnownMachine>`;
    - constants `NotFound`, `InvalidAnswer`.
  - `InviteText.Ended(InviteOutcome outcome) → string`

- [ ] **Step 1: Write the failing tests**

Create `tests/Midview.Master.Tests/InviteTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Both teachers' sides over loopback, with the machines faked (giveClass).
public class InviteTests : IDisposable
{
    private static readonly TimeSpan Listen = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private static readonly InviteResult TheClass = new(
        new[] { new InvitedMachine("ALUNO-01", "10.0.0.1", new string('5', 40), new string('A', 40), "00-1A-2B-3C-4D-5E") },
        new[] { new LeftOutMachine("ALUNO-07", "Offline") });

    private readonly X509Certificate2 _teacherA = SelfSignedCertificate.Create("teacher-a");
    private readonly X509Certificate2 _teacherB = SelfSignedCertificate.Create("teacher-b");
    private readonly List<InviteHost> _hosts = new();
    private readonly List<TeacherGrant> _grants = new();

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
        _teacherA.Dispose();
        _teacherB.Dispose();
    }

    private InviteHost Open(
        X509Certificate2? certificate = null, string name = "PROF-01", TimeSpan? lifetime = null,
        Func<TeacherGrant, InviteResult>? giveClass = null, Action<string>? joining = null)
    {
        var host = InviteHost.Open(certificate ?? _teacherA, name, giveClass ?? Record, lifetime ?? Long, joining, tcpPort: 0, udpPort: 0);
        _hosts.Add(host);
        return host;
    }

    private InviteResult Record(TeacherGrant grant)
    {
        lock (_grants)
        {
            _grants.Add(grant);
        }
        return TheClass;
    }

    private static IReadOnlyList<IPEndPoint> Targets(params InviteHost[] hosts) =>
        hosts.Select(host => new IPEndPoint(IPAddress.Loopback, host.UdpPort)).ToList();

    private Task<(string From, InviteResult Result)> Join(string code, X509Certificate2 certificate, params InviteHost[] hosts) =>
        Task.Run(() => InviteJoin.Join(code, certificate, "PROF-02", Targets(hosts), Listen)).WaitAsync(Wait);

    [Fact]
    public async Task TheRightCode_GivesTheColleagueTheClass()
    {
        var host = Open();
        var progress = new List<string>();

        var (from, result) = await Task.Run(() => InviteJoin.Join(host.Code, _teacherB, "PROF-02", Targets(host), Listen, progress.Add))
            .WaitAsync(Wait);

        Assert.Equal("PROF-01", from);
        Assert.Equal(TheClass.Given, result.Given);
        Assert.Equal(TheClass.LeftOut, result.LeftOut);
        Assert.Equal(new TeacherGrant(_teacherB.Thumbprint, "PROF-02"), _grants.Single());
        var outcome = await host.Outcome.WaitAsync(Wait);
        Assert.Equal(InviteEnd.Given, outcome.End);
        Assert.Equal("PROF-02", outcome.TeacherName);
        Assert.True(outcome.ListSent);
        Assert.Equal(
            new[] { "Procurando o professor na rede...", "Conectando a PROF-01...", "PROF-01 está liberando as máquinas..." },
            progress);
    }

    [Fact]
    public async Task AWrongSecret_IsRefused_AndEndsTheInvite()
    {
        var host = Open();
        var secret = TeacherInvite.Secret(host.Code) == "ZZZZ" ? "YYYY" : "ZZZZ";
        var wrong = secret + TeacherInvite.Check(_teacherA.Thumbprint, secret);

        var refused = await Assert.ThrowsAsync<InviteJoinException>(() => Join(wrong, _teacherB, host));

        Assert.Equal(InviteHost.WrongCodeText, refused.Message);
        Assert.Equal(InviteEnd.WrongCode, (await host.Outcome.WaitAsync(Wait)).End);
        Assert.Empty(_grants);
        var afterwards = await Assert.ThrowsAsync<InviteJoinException>(() => Join(host.Code, _teacherB, host));
        Assert.Equal(InviteJoin.NotFound, afterwards.Message);
    }

    [Fact]
    public async Task ACodeForAnotherCertificate_NeverSendsTheSecret()
    {
        using var someoneElse = SelfSignedCertificate.Create("someone-else");
        var host = Open();
        var secret = TeacherInvite.Secret(host.Code);
        var elsewhere = secret + TeacherInvite.Check(someoneElse.Thumbprint, secret);

        var refused = await Assert.ThrowsAsync<InviteJoinException>(() => Join(elsewhere, _teacherB, host));

        // Had the secret (the right one) been sent, the class would be given.
        Assert.Equal(InviteJoin.NotFound, refused.Message);
        Assert.False(host.Outcome.IsCompleted);
        Assert.Empty(_grants);
    }

    [Fact]
    public async Task TheInviteExpires()
    {
        var host = Open(lifetime: TimeSpan.FromMilliseconds(300));

        Assert.Equal(InviteEnd.Expired, (await host.Outcome.WaitAsync(Wait)).End);
        var refused = await Assert.ThrowsAsync<InviteJoinException>(() => Join(host.Code, _teacherB, host));
        Assert.Equal(InviteJoin.NotFound, refused.Message);
    }

    [Fact]
    public async Task ClosingTheWindow_CancelsTheInvite_AndStopsAnswering()
    {
        var host = Open();
        Assert.Single(InviteJoin.Find(Targets(host), Listen));

        host.Dispose();

        Assert.Equal(InviteEnd.Cancelled, (await host.Outcome.WaitAsync(Wait)).End);
        Assert.Empty(InviteJoin.Find(Targets(host), Listen));
    }

    [Fact]
    public async Task TheSameIdentity_IsRefused_AndTheInviteStaysOpen()
    {
        var host = Open();

        var refused = await Assert.ThrowsAsync<InviteJoinException>(() => Join(host.Code, _teacherA, host));

        Assert.Equal(InviteHost.SameIdentityText("PROF-01"), refused.Message);
        Assert.False(host.Outcome.IsCompleted);
        var (from, _) = await Join(host.Code, _teacherB, host);
        Assert.Equal("PROF-01", from);
    }

    [Fact]
    public async Task OnlyOneColleagueAtATime()
    {
        using var release = new ManualResetEventSlim();
        using var joined = new ManualResetEventSlim();
        var host = Open(
            giveClass: grant =>
            {
                release.Wait(Wait);
                return Record(grant);
            },
            joining: _ => joined.Set());

        var first = Join(host.Code, _teacherB, host);
        Assert.True(joined.Wait(Wait));

        // A second connection meanwhile is closed unanswered.
        using var tcp = new TcpClient();
        tcp.Connect(IPAddress.Loopback, host.TcpPort);
        tcp.ReceiveTimeout = 10_000;
        Assert.ThrowsAny<Exception>(() =>
        {
            using var channel = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, "localhost", _teacherB);
            channel.Receive();
        });

        release.Set();
        var (from, _) = await first;
        Assert.Equal("PROF-01", from);
    }

    [Fact]
    public async Task WithTwoInvitesOpen_TheCodeFindsItsOwnTeacher()
    {
        using var teacherC = SelfSignedCertificate.Create("teacher-c");
        var other = Open(teacherC, "PROF-03");
        var host = Open();

        var (from, _) = await Join(host.Code, _teacherB, other, host);

        Assert.Equal("PROF-01", from);
        Assert.Equal(InviteEnd.Given, (await host.Outcome.WaitAsync(Wait)).End);
        Assert.False(other.Outcome.IsCompleted);
    }

    [Fact]
    public void APortInUse_CannotOpenASecondInvite()
    {
        var host = Open();

        Assert.Throws<SocketException>(() => InviteHost.Open(_teacherA, "PROF-01", Record, Long, tcpPort: host.TcpPort, udpPort: 0));
    }

    [Fact]
    public void TheMachinesThatCame_GoIntoTheClassroomListAsTheyAre()
    {
        var result = new InviteResult(
            new[] { new InvitedMachine("ALUNO-01", "PC-ALUNO-01", new string('e', 40), new string('a', 40), "00:1a:2b:3c:4d:5e") },
            Array.Empty<LeftOutMachine>());

        Assert.Equal(
            new KnownMachine("ALUNO-01", "PC-ALUNO-01", new string('E', 40), new string('A', 40), "00-1A-2B-3C-4D-5E"),
            InviteJoin.ToKnownMachines(result).Single());
    }
}
```

Add to `tests/Midview.Master.Tests/InviteTextTests.cs`, inside the class:

```csharp
    [Fact]
    public void Ended_SaysHowTheInviteEnded()
    {
        var one = new InviteResult(new[] { Machine("ALUNO-01") }, Array.Empty<LeftOutMachine>());

        Assert.Equal("PROF-02 recebeu acesso a 1 de 1 máquinas.", InviteText.Ended(new InviteOutcome(InviteEnd.Given, "PROF-02", one, ListSent: true)));
        Assert.Equal(
            "PROF-02 recebeu acesso a 1 de 1 máquinas. A conexão caiu antes de a lista chegar ao colega: gere outro código para ele (as máquinas não se repetem).",
            InviteText.Ended(new InviteOutcome(InviteEnd.Given, "PROF-02", one, ListSent: false)));
        Assert.Equal(
            "Alguém na rede tentou entrar com um código errado. O convite foi cancelado. Gere outro se ainda precisar.",
            InviteText.Ended(new InviteOutcome(InviteEnd.WrongCode)));
        Assert.Equal("O código expirou. Gere outro se ainda precisar.", InviteText.Ended(new InviteOutcome(InviteEnd.Expired)));
        Assert.Equal("O convite foi cancelado.", InviteText.Ended(new InviteOutcome(InviteEnd.Cancelled)));
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Master.Tests --filter "FullyQualifiedName~InviteTests|FullyQualifiedName~InviteTextTests"`
Expected: build FAILS with `CS0103: The name 'InviteHost' does not exist`.

- [ ] **Step 3: Share the UDP reset switch**

In `src/Midview.Master/DiscoveryClient.cs`, change:

```csharp
    private const int SioUdpConnReset = -1744830452;
```

to:

```csharp
    internal const int SioUdpConnReset = -1744830452;
```

(Its comment stays. `InviteHost` and `InviteJoin` use it too.)

- [ ] **Step 4: Write `InviteHost.cs`**

Create `src/Midview.Master/InviteHost.cs`:

```csharp
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

public enum InviteEnd
{
    // The colleague got the class.
    Given,

    // Someone sent a wrong secret: the invite ended at once.
    WrongCode,

    Expired,
    Cancelled,
}

// How an invite ended. Given: TeacherName got Result's machines; ListSent
// is false if the connection dropped before the list reached them.
public sealed record InviteOutcome(InviteEnd End, string? TeacherName = null, InviteResult? Result = null, bool ListSent = false);

// Teacher A's side of "Dar acesso a um colega" (see TeacherInvite): makes
// the code, answers "who has an invite open?" on UDP and takes one colleague
// over TLS, only while the invite lasts. The code's secret is the
// permission: whoever sends it gets the class (giveClass tells every
// machine to trust them, and says which did). One connection at a time; the
// first wrong secret ends the invite.
public sealed class InviteHost : IDisposable
{
    public const string WrongCodeText = "Código errado. O convite foi cancelado; peça outro ao colega.";
    public const string GoneText = "O convite expirou ou foi cancelado. Peça outro código ao colega.";

    // _state: open, then either ended (expired, cancelled, wrong secret) or
    // handing over (the right secret came) and then ended. Only one of them
    // wins the switch from open, so an invite that expires while the class
    // is being given still ends as given.
    private const int StateOpen = 0;
    private const int StateHandingOver = 1;
    private const int StateEnded = 2;

    private readonly X509Certificate2 _certificate;
    private readonly string _teacherName;
    private readonly Func<TeacherGrant, InviteResult> _giveClass;
    private readonly Action<string>? _joining;
    private readonly TcpListener _tcp;
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<InviteOutcome> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state = StateOpen;
    private int _busy;
    private volatile bool _cancelled;

    private InviteHost(
        X509Certificate2 certificate, string teacherName, Func<TeacherGrant, InviteResult> giveClass,
        Action<string>? joining, TcpListener tcp, UdpClient udp)
    {
        _certificate = certificate;
        _teacherName = teacherName;
        _giveClass = giveClass;
        _joining = joining;
        _tcp = tcp;
        _udp = udp;
        Code = TeacherInvite.NewCode(certificate.Thumbprint);
        TcpPort = ((IPEndPoint)tcp.LocalEndpoint).Port;
        UdpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }

    public string Code { get; }

    public int TcpPort { get; }

    public int UdpPort { get; }

    public Task<InviteOutcome> Outcome => _outcome.Task;

    // Opens the invite for lifetime. joining gets the colleague's computer
    // name (on a background thread) once the right secret arrived, before
    // the machines are told. Throws SocketException when a port can't be
    // had (port 0, for tests, takes any free one).
    public static InviteHost Open(
        X509Certificate2 certificate, string teacherName, Func<TeacherGrant, InviteResult> giveClass, TimeSpan lifetime,
        Action<string>? joining = null, int tcpPort = TeacherInvite.Port, int udpPort = TeacherInvite.Port)
    {
        var tcp = new TcpListener(IPAddress.Any, tcpPort);
        tcp.Start();
        UdpClient? udp = null;
        try
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, udpPort));
            udp.Client.IOControl(DiscoveryClient.SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
        }
        catch
        {
            udp?.Dispose();
            tcp.Stop();
            throw;
        }

        var host = new InviteHost(certificate, teacherName, giveClass, joining, tcp, udp);
        host._stop.Token.Register(host.OnStopped);
        host._stop.CancelAfter(lifetime);
        _ = host.AcceptAsync();
        _ = host.AnswerProbesAsync();
        return host;
    }

    public static string SameIdentityText(string teacherName) =>
        $"Este computador já usa a mesma identidade de professor que {teacherName}; ele já tem acesso às mesmas máquinas.";

    // Closing the window: the invite ends, unless the class is already being
    // given (that finishes, and Outcome says so).
    public void Dispose()
    {
        _cancelled = true;
        _stop.Cancel();
    }

    private void OnStopped()
    {
        if (Interlocked.CompareExchange(ref _state, StateEnded, StateOpen) == StateOpen)
        {
            Finish(new InviteOutcome(_cancelled ? InviteEnd.Cancelled : InviteEnd.Expired));
        }
    }

    private void Finish(InviteOutcome outcome)
    {
        if (!_outcome.TrySetResult(outcome))
        {
            return;
        }
        _tcp.Stop();
        _udp.Dispose();
        _stop.Cancel();
    }

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _tcp.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            // One colleague at a time: another connection meanwhile is
            // closed unanswered.
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                client.Dispose();
                continue;
            }

            // Its own thread, as the Agent does: the exchange blocks on the
            // network, and giving the class waits on every machine.
            _ = Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        Handle(client);
                    }
                    finally
                    {
                        client.Dispose();
                        Volatile.Write(ref _busy, 0);
                    }
                },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    private void Handle(TcpClient client)
    {
        var timeout = (int)TeacherInvite.JoinTimeout.TotalMilliseconds;
        client.ReceiveTimeout = timeout;
        client.SendTimeout = timeout;
        try
        {
            using var channel = SecureChannel.AuthenticateAsServerAcceptingAnyClient(client, _certificate);
            var message = channel.Receive(TeacherInvite.MaxJoinLength);
            if (message.Type != MessageType.InviteJoin || InviteJoinRequest.Decode(message.Payload) is not { } join)
            {
                return;
            }

            if (!TeacherInvite.SameSecret(join.Secret, TeacherInvite.Secret(Code)))
            {
                // An honest colleague's typo never gets here (their side
                // checks the code against this certificate first): this is
                // someone guessing.
                var first = Interlocked.CompareExchange(ref _state, StateEnded, StateOpen) == StateOpen;
                TrySend(channel, TeacherInvite.Refused(first ? WrongCodeText : GoneText));
                if (first)
                {
                    Finish(new InviteOutcome(InviteEnd.WrongCode));
                }
                return;
            }

            // A copy of this teacher's own master-identity.pfx: it already
            // reaches every machine this one does.
            if (string.Equals(channel.RemoteThumbprint, _certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                channel.Send(TeacherInvite.Refused(SameIdentityText(_teacherName)));
                return;
            }

            if (Interlocked.CompareExchange(ref _state, StateHandingOver, StateOpen) != StateOpen)
            {
                channel.Send(TeacherInvite.Refused(GoneText));
                return;
            }

            GiveTheClass(channel, Pairing.CleanTeacherName(join.TeacherName));
        }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or InvalidDataException or ObjectDisposedException)
        {
            // A colleague whose code points elsewhere hangs up after the
            // handshake, and a stranger may send nothing: the invite stays
            // open.
        }
    }

    private void GiveTheClass(SecureChannel channel, string teacherName)
    {
        var result = InviteResult.Empty;
        var sent = false;
        try
        {
            _joining?.Invoke(teacherName);
            result = _giveClass(new TeacherGrant(channel.RemoteThumbprint, teacherName));
            channel.Send(new Message(MessageType.InviteDone, result.Encode()));
            sent = true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // The colleague left; the machines were told anyway.
        }
        finally
        {
            Volatile.Write(ref _state, StateEnded);
            Finish(new InviteOutcome(InviteEnd.Given, teacherName, result, sent));
        }
    }

    private static void TrySend(SecureChannel channel, Message message)
    {
        try
        {
            channel.Send(message);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // Gone already.
        }
    }

    private async Task AnswerProbesAsync()
    {
        while (true)
        {
            UdpReceiveResult received;
            try
            {
                received = await _udp.ReceiveAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // A reset left by an earlier reply that couldn't be
                // delivered, or the socket closing as the invite ends.
                if (_stop.IsCancellationRequested)
                {
                    return;
                }
                continue;
            }

            if (TeacherInvite.ParseProbe(received.Buffer) is not { } probe)
            {
                continue;
            }

            try
            {
                await _udp.SendAsync(TeacherInvite.EncodeReply(probe, _teacherName, TcpPort), received.RemoteEndPoint, _stop.Token);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The colleague's PC went away, or the invite ended.
            }
        }
    }
}
```

- [ ] **Step 5: Write `InviteJoin.cs`**

Create `src/Midview.Master/InviteJoin.cs`:

```csharp
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// What the colleague should read: no teacher has that code open, the other
// teacher refused, or the connection dropped.
public sealed class InviteJoinException(string message) : Exception(message);

// Teacher B's side of "Dar acesso a um colega" (see TeacherInvite): find
// who has an invite open, find the one the code points to, send it the
// secret, and get its class.
public static class InviteJoin
{
    public const string NotFound =
        "Nenhum professor na rede tem esse código aberto. Confira o código com o colega e se o Midview Professor dele está aberto, com o código na tela.";
    public const string InvalidAnswer = "O outro professor respondeu de um jeito inválido. Peça outro código e tente de novo.";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // code: as TeacherInvite.TryRead gave it. probeTargets: the broadcasts
    // (WakeOnLanSender.BroadcastTargets with TeacherInvite.Port). progress
    // gets what to show while it works. Returns the computer name of the
    // teacher who gave the class, and the class.
    public static (string From, InviteResult Result) Join(
        string code, X509Certificate2 certificate, string teacherName,
        IReadOnlyList<IPEndPoint> probeTargets, TimeSpan listenTime, Action<string>? progress = null)
    {
        progress?.Invoke("Procurando o professor na rede...");
        foreach (var (endpoint, name) in Find(probeTargets, listenTime))
        {
            progress?.Invoke($"Conectando a {name}...");
            if (Connect(endpoint, certificate) is not { } connection)
            {
                continue;
            }

            using (connection)
            {
                // The code names one certificate: anyone else never hears
                // the secret.
                if (!TeacherInvite.PointsTo(code, connection.Channel.RemoteThumbprint))
                {
                    continue;
                }
                return (name, Ask(connection, code, teacherName, name, progress));
            }
        }
        throw new InviteJoinException(NotFound);
    }

    // Who answered "who has an invite open?": where to connect (the
    // answer's address, on the port it named) and their computer name. At
    // most TeacherInvite.MaxResponders, in the order they answered.
    public static IReadOnlyList<(IPEndPoint Endpoint, string Name)> Find(IReadOnlyList<IPEndPoint> targets, TimeSpan listenTime)
    {
        var id = Guid.NewGuid();
        var packet = TeacherInvite.EncodeProbe(id);
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        udp.Client.IOControl(DiscoveryClient.SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
        foreach (var target in targets)
        {
            try
            {
                udp.Send(packet, packet.Length, target);
            }
            catch (SocketException)
            {
                // A card that just went down: the other targets still go.
            }
        }

        var found = new List<(IPEndPoint Endpoint, string Name)>();
        var deadline = DateTime.UtcNow + listenTime;
        while (found.Count < TeacherInvite.MaxResponders && deadline - DateTime.UtcNow is var left && left > TimeSpan.Zero)
        {
            try
            {
                using var timeout = new CancellationTokenSource(left);
                var received = udp.ReceiveAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                if (TeacherInvite.ParseReply(received.Buffer) is { } reply && reply.ProbeId == id)
                {
                    var endpoint = new IPEndPoint(received.RemoteEndPoint.Address, reply.Port);
                    if (!found.Any(entry => entry.Endpoint.Equals(endpoint)))
                    {
                        found.Add((endpoint, reply.Name));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                // Keep listening.
            }
        }
        return found;
    }

    // The colleague's machines, as this classroom list keeps them.
    public static IReadOnlyList<KnownMachine> ToKnownMachines(InviteResult result) =>
        result.Given
            .Select(machine => new KnownMachine(
                machine.Name, machine.HostAddress, machine.SessionThumbprint.ToUpperInvariant(),
                machine.AgentThumbprint.ToUpperInvariant(), WakeOnLan.NormalizeMacAddress(machine.MacAddress)))
            .ToList();

    private static Connection? Connect(IPEndPoint endpoint, X509Certificate2 certificate)
    {
        TcpClient? tcp = null;
        try
        {
            tcp = TcpConnect.WithTimeout(endpoint.Address.ToString(), endpoint.Port, ConnectTimeout);
            var timeout = (int)TeacherInvite.JoinTimeout.TotalMilliseconds;
            tcp.ReceiveTimeout = timeout;
            tcp.SendTimeout = timeout;
            return new Connection(tcp, SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, endpoint.Address.ToString(), certificate));
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException or OperationCanceledException)
        {
            tcp?.Dispose();
            return null;
        }
    }

    private static InviteResult Ask(Connection connection, string code, string teacherName, string from, Action<string>? progress)
    {
        Message answer;
        try
        {
            connection.Channel.Send(new Message(
                MessageType.InviteJoin, new InviteJoinRequest(TeacherInvite.Secret(code), teacherName).Encode()));
            connection.Tcp.ReceiveTimeout = (int)TeacherInvite.DoneTimeout.TotalMilliseconds;
            progress?.Invoke($"{from} está liberando as máquinas...");
            answer = connection.Channel.Receive();
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException)
        {
            throw new InviteJoinException(
                $"A conexão com {from} caiu antes de a lista da sala chegar ({FailureText.DescribeAgent(ex)}). "
                + "Peça outro código ao colega: as máquinas que você já tem não se repetem.");
        }

        return answer.Type switch
        {
            MessageType.InviteDone => InviteResult.Decode(answer.Payload) ?? throw new InviteJoinException(InvalidAnswer),
            MessageType.InviteRefused => throw new InviteJoinException(TeacherInvite.RefusalText(answer.Payload)),
            _ => throw new InviteJoinException(InvalidAnswer),
        };
    }

    private sealed class Connection(TcpClient tcp, SecureChannel channel) : IDisposable
    {
        public TcpClient Tcp { get; } = tcp;

        public SecureChannel Channel { get; } = channel;

        public void Dispose()
        {
            Channel.Dispose();
            Tcp.Dispose();
        }
    }
}
```

- [ ] **Step 6: Add `InviteText.Ended`**

In `src/Midview.Master/InviteText.cs`, after `Waiting`:

```csharp

    // The teacher who gave the class, once the invite is over.
    public static string Ended(InviteOutcome outcome) => outcome.End switch
    {
        InviteEnd.Given when !outcome.ListSent =>
            $"{Given(outcome.TeacherName!, outcome.Result!)} A conexão caiu antes de a lista chegar ao colega: gere outro código para ele (as máquinas não se repetem).",
        InviteEnd.Given => Given(outcome.TeacherName!, outcome.Result!),
        InviteEnd.WrongCode => "Alguém na rede tentou entrar com um código errado. O convite foi cancelado. Gere outro se ainda precisar.",
        InviteEnd.Expired => "O código expirou. Gere outro se ainda precisar.",
        _ => "O convite foi cancelado.",
    };
```

- [ ] **Step 7: Run the tests to see them pass**

Run: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test tests/Midview.Master.Tests --filter "FullyQualifiedName~InviteTests|FullyQualifiedName~InviteTextTests"`
Expected: PASS. Windows Firewall may ask once about the test host listening. The tests use loopback only, so allowing or cancelling makes no difference to them.

Then the whole suite: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test Midview.sln`
Expected: PASS, with only the 3 old `xUnit1031` warnings.

- [ ] **Step 8: Commit**

```bash
git add src/Midview.Master/InviteHost.cs src/Midview.Master/InviteJoin.cs src/Midview.Master/DiscoveryClient.cs src/Midview.Master/InviteText.cs tests/Midview.Master.Tests/InviteTests.cs tests/Midview.Master.Tests/InviteTextTests.cs
git commit -F - <<'EOF'
Open an invite on the teacher's PC and join it from a colleague's

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: Master: the two windows and where they open

**Files:**
- Create: `src/Midview.Master/GiveAccessWindow.xaml`, `src/Midview.Master/GiveAccessWindow.xaml.cs`
- Create: `src/Midview.Master/JoinClassWindow.xaml`, `src/Midview.Master/JoinClassWindow.xaml.cs`
- Modify: `src/Midview.Master/MachinesWindow.xaml` (the "Procurar e parear" and "Outro professor" sections)
- Modify: `src/Midview.Master/MachinesWindow.xaml.cs` (constructor, two click handlers)
- Modify: `src/Midview.Master/MainWindow.xaml` (empty-room panel, "Máquinas da sala" tooltip)
- Modify: `src/Midview.Master/MainWindow.xaml.cs` (`OpenMachinesWindow`, `AddPairedMachine`, new openers)
- Modify: `src/Midview.Master/MasterIdentity.cs` (comment only)

**Interfaces:**
- Consumes:
  - `InviteHost.Open`, `InviteHost.Code`, `InviteHost.Outcome`, `InviteHost.Dispose` (Task 4);
  - `InviteJoin.Join`, `InviteJoin.ToKnownMachines`, `InviteJoinException` (Task 4);
  - `ClassGrant.Give`, `ClassGrant.OverTheNetwork` (Task 3);
  - `InviteText.Waiting`, `InviteText.Ended`, `InviteText.Received` (Tasks 3 and 4);
  - `TeacherInvite.TryRead`, `TeacherInvite.Format`, `TeacherInvite.BadCode`, `TeacherInvite.Lifetime`, `TeacherInvite.ListenTime`, `TeacherInvite.Port` (Task 1);
  - `WakeOnLanSender.BroadcastTargets`, `WakeOnLanSender.LocalNetworks`, `ShowOnce`, `Roster.Place`, `ErrorText.Describe` (existing).
- Produces: the user-facing windows. No later task consumes them.

This task has no unit tests: the logic it calls is covered by Tasks 1-4, and the windows are checked by building and by the manual run in Step 7.

- [ ] **Step 1: Write `GiveAccessWindow`**

Create `src/Midview.Master/GiveAccessWindow.xaml`:

```xml
<Window x:Class="Midview.Master.GiveAccessWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Dar acesso a um colega" Height="360" Width="500"
        WindowStartupLocation="CenterOwner"
        Background="{StaticResource CanvasBrush}" FontFamily="{StaticResource UiFont}"
        Loaded="OnLoaded" Closed="OnClosed">
    <StackPanel Margin="20">
        <TextBlock x:Name="LeadTextBlock" Text="Passe este código ao colega:" FontSize="14" Foreground="{StaticResource InkBrush}" />
        <TextBlock x:Name="CodeTextBlock" FontFamily="Consolas" FontSize="40" FontWeight="Bold"
                   Foreground="{StaticResource InkBrush}" Margin="0,10,0,10" />
        <TextBlock x:Name="HintTextBlock" TextWrapping="Wrap" Foreground="{StaticResource InkMutedBrush}" />
        <TextBlock x:Name="StatusTextBlock" TextWrapping="Wrap" Margin="0,12,0,0" Foreground="{StaticResource InkBrush}" />
        <Button x:Name="CloseButton" Content="Cancelar" Click="OnCloseClicked"
                HorizontalAlignment="Left" Margin="0,16,0,0" Padding="14,5" />
    </StackPanel>
</Window>
```

Create `src/Midview.Master/GiveAccessWindow.xaml.cs`:

```csharp
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Dar acesso a um colega", for the teacher who has the class (InviteHost):
// shows the code while the invite lasts, then who got the class. Closing it
// cancels the invite.
public partial class GiveAccessWindow : Window
{
    private readonly X509Certificate2 _masterCertificate;
    private readonly Func<IReadOnlyList<KnownMachine>> _roster;
    private InviteHost? _host;

    public GiveAccessWindow(X509Certificate2 masterCertificate, Func<IReadOnlyList<KnownMachine>> roster)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        _roster = roster;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The class as it is now: a machine added while the code is on the
        // screen goes with the next invite.
        var machines = _roster();
        if (machines.Count == 0)
        {
            ShowOver("A lista da sala está vazia: não há máquinas para dar ao colega.");
            return;
        }

        try
        {
            _host = InviteHost.Open(
                _masterCertificate, Environment.MachineName,
                grant => ClassGrant.Give(machines, ClassGrant.OverTheNetwork(_masterCertificate, grant)),
                TeacherInvite.Lifetime,
                joining: name => Dispatcher.InvokeAsync(() => StatusTextBlock.Text = $"{name} entrou. Liberando as máquinas..."));
        }
        catch (SocketException ex)
        {
            ShowOver($"Não foi possível abrir o convite neste computador: {ErrorText.Describe(ex)}");
            return;
        }

        CodeTextBlock.Text = TeacherInvite.Format(_host.Code);
        HintTextBlock.Text = InviteText.Waiting(machines.Count, DateTime.Now + TeacherInvite.Lifetime);
        var outcome = await _host.Outcome;
        LeadTextBlock.Text = "O código não vale mais.";
        CodeTextBlock.Text = string.Empty;
        HintTextBlock.Text = string.Empty;
        StatusTextBlock.Text = InviteText.Ended(outcome);
        CloseButton.Content = "Fechar";
    }

    private void ShowOver(string text)
    {
        LeadTextBlock.Text = text;
        CodeTextBlock.Visibility = Visibility.Collapsed;
        CloseButton.Content = "Fechar";
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e) => _host?.Dispose();
}
```

- [ ] **Step 2: Write `JoinClassWindow`**

Create `src/Midview.Master/JoinClassWindow.xaml`:

```xml
<Window x:Class="Midview.Master.JoinClassWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Entrar na sala de um colega" Height="320" Width="500"
        WindowStartupLocation="CenterOwner"
        Background="{StaticResource CanvasBrush}" FontFamily="{StaticResource UiFont}"
        FocusManager.FocusedElement="{Binding ElementName=CodeTextBox}">
    <StackPanel Margin="20">
        <TextBlock TextWrapping="Wrap" Foreground="{StaticResource InkMutedBrush}"
                   Text="Peça ao colega que já tem a sala para clicar em &quot;Máquinas da sala&quot; &gt; &quot;Dar acesso a um colega&quot;, e digite aqui o código que aparecer na tela dele." />
        <TextBlock Text="Código do convite:" Margin="0,14,0,4" Foreground="{StaticResource InkBrush}" />
        <StackPanel Orientation="Horizontal">
            <TextBox x:Name="CodeTextBox" Width="200" FontFamily="Consolas" FontSize="20" Padding="6,3"
                     MaxLength="20" CharacterCasing="Upper" />
            <Button x:Name="JoinButton" Content="Entrar" Click="OnJoinClicked" IsDefault="True"
                    Margin="8,0,0,0" Padding="16,4" />
        </StackPanel>
        <TextBlock x:Name="StatusTextBlock" TextWrapping="Wrap" Margin="0,14,0,0" Foreground="{StaticResource InkBrush}" />
    </StackPanel>
</Window>
```

Create `src/Midview.Master/JoinClassWindow.xaml.cs`:

```csharp
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Media;
using Midview.Protocol;

namespace Midview.Master;

// "Entrar na sala de um colega" (InviteJoin): type the code a colleague
// reads out, and that colleague's class comes into this one.
public partial class JoinClassWindow : Window
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E));

    private readonly X509Certificate2 _masterCertificate;
    private readonly Action<IReadOnlyList<KnownMachine>> _addMachines;

    // addMachines puts the machines in the classroom list (MainWindow); it
    // may throw when the list can't be written to disk.
    public JoinClassWindow(X509Certificate2 masterCertificate, Action<IReadOnlyList<KnownMachine>> addMachines)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        _addMachines = addMachines;
    }

    private async void OnJoinClicked(object sender, RoutedEventArgs e)
    {
        if (!TeacherInvite.TryRead(CodeTextBox.Text, out var code))
        {
            ShowStatus(TeacherInvite.BadCode, isError: true);
            CodeTextBox.Focus();
            return;
        }

        JoinButton.IsEnabled = false;
        CodeTextBox.IsEnabled = false;
        IProgress<string> progress = new Progress<string>(text => ShowStatus(text, isError: false));
        try
        {
            var teacherName = Environment.MachineName;
            var targets = WakeOnLanSender.BroadcastTargets(WakeOnLanSender.LocalNetworks(), TeacherInvite.Port);
            var (_, result) = await Task.Run(() => InviteJoin.Join(
                code, _masterCertificate, teacherName, targets, TeacherInvite.ListenTime, progress.Report));
            try
            {
                _addMachines(InviteJoin.ToKnownMachines(result));
                ShowStatus(InviteText.Received(result), isError: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowStatus($"{InviteText.Received(result)} Mas a lista da sala não pôde ser gravada: {ErrorText.Describe(ex)}", isError: true);
            }
        }
        catch (InviteJoinException ex)
        {
            ShowStatus(ex.Message, isError: true);
            JoinButton.IsEnabled = true;
            CodeTextBox.IsEnabled = true;
        }
        catch (SocketException ex)
        {
            ShowStatus($"A procura falhou: {ErrorText.Describe(ex)}", isError: true);
            JoinButton.IsEnabled = true;
            CodeTextBox.IsEnabled = true;
        }
    }

    private void ShowStatus(string text, bool isError)
    {
        StatusTextBlock.Text = text;
        StatusTextBlock.Foreground = isError ? ErrorBrush : (Brush)FindResource("InkBrush");
    }
}
```

- [ ] **Step 3: Add the entry points to "Máquinas da sala"**

In `src/Midview.Master/MachinesWindow.xaml`, in the top `StackPanel` ("Procurar e parear"), after the horizontal `StackPanel` holding `SearchButton`, `PairButton` and `SearchStatusTextBlock`, add:

```xml
            <TextBlock Margin="0,8,0,0" Foreground="{StaticResource InkMutedBrush}">
                <Run Text="Um colega já tem estas máquinas?" />
                <Hyperlink Click="OnJoinClassClicked">Entrar na sala de um colega...</Hyperlink>
            </TextBlock>
```

In the "Outro professor" section, replace:

```xml
                    <TextBlock Style="{StaticResource SectionHint}"
                               Text="Para um colega parear o computador dele com as máquinas desta sala: por 10 minutos, elas aceitam parear com outro professor." />
                    <Button x:Name="AllowNewTeacherButton" Content="Permitir novo professor (10 minutos)" Click="OnAllowNewTeacherClicked" HorizontalAlignment="Left" />
```

with:

```xml
                    <TextBlock Style="{StaticResource SectionHint}"
                               Text="Para um colega ter as máquinas desta sala: dê a ele um código (nada aparece na tela dos alunos), ou deixe as máquinas aceitarem o pareamento dele por 10 minutos." />
                    <StackPanel Orientation="Horizontal">
                        <Button Content="Dar acesso a um colega..." Click="OnGiveAccessClicked" />
                        <Button x:Name="AllowNewTeacherButton" Content="Permitir novo professor (10 minutos)" Click="OnAllowNewTeacherClicked" Margin="8,0,0,0" />
                    </StackPanel>
```

In `src/Midview.Master/MachinesWindow.xaml.cs`:

1. Next to `private readonly Action _openAdvanced;`, add:

```csharp
    private readonly Action _openGiveAccess;
    private readonly Action _openJoinClass;
```

2. Change the constructor's signature and body: add the two parameters and keep the rest as it is.

```csharp
    // addPaired and refreshAddresses change the classroom list (MainWindow);
    // both may throw when the list can't be written to disk.
    public MachinesWindow(
        X509Certificate2 masterCertificate, Func<IReadOnlyList<KnownMachine>> roster,
        Action<KnownMachine> addPaired, Func<IReadOnlyList<FoundMachine>, int> refreshAddresses, Action openAdvanced,
        Action openGiveAccess, Action openJoinClass)
    {
        InitializeComponent();
        _openAdvanced = openAdvanced;
        _openGiveAccess = openGiveAccess;
        _openJoinClass = openJoinClass;
        _masterCertificate = masterCertificate;
        _roster = roster;
        _addPaired = addPaired;
        _refreshAddresses = refreshAddresses;
        FoundItemsControl.ItemsSource = _rows;
        ShowStudentProgramVersion();
    }
```

3. After `private void OnAdvancedClicked(object sender, RoutedEventArgs e) => _openAdvanced();`, add:

```csharp

    private void OnGiveAccessClicked(object sender, RoutedEventArgs e) => _openGiveAccess();

    private void OnJoinClassClicked(object sender, RoutedEventArgs e) => _openJoinClass();
```

4. Update the class comment's first sentence so it lists the new parts:

```csharp
// "Máquinas da sala": find the machines with Midview on this network and
// pair with them (each shows a code the teacher compares), push the student
// program installed with this one to the class, give a colleague the class
// with a code or take a colleague's (GiveAccessWindow, JoinClassWindow), let
// another teacher pair for 10 minutes, and open the advanced settings
// (AddMachineWindow).
```

- [ ] **Step 4: Wire them in `MainWindow`**

In `src/Midview.Master/MainWindow.xaml.cs`:

1. In `OpenMachinesWindow`, replace the construction:

```csharp
            window = new MachinesWindow(
                _masterCertificate, () => _tiles.Select(tile => tile.Machine).ToList(), AddPairedMachine, RefreshAddresses,
                () => OpenAddMachineWindow(existing: null)) { Owner = this };
```

with:

```csharp
            window = new MachinesWindow(
                _masterCertificate, () => _tiles.Select(tile => tile.Machine).ToList(), AddPairedMachine, RefreshAddresses,
                () => OpenAddMachineWindow(existing: null), OpenGiveAccessWindow, OpenJoinClassWindow) { Owner = this };
```

2. Replace `AddPairedMachine`, keeping its comment, with:

```csharp
    // Called by MachinesWindow when a pairing is confirmed (see Roster.Place).
    // The grid changes first; a failure to write the list then propagates
    // for MachinesWindow to report.
    private void AddPairedMachine(KnownMachine machine) => AddMachines(new[] { machine });

    // Also called by JoinClassWindow with a colleague's class: each machine
    // placed as a paired one (Roster.Place, in the colleague's order), then
    // the list saved once.
    private void AddMachines(IReadOnlyList<KnownMachine> machines)
    {
        foreach (var machine in machines)
        {
            var (index, replaces, placed) = Roster.Place(_tiles.Select(tile => tile.Machine).ToList(), machine);
            if (replaces)
            {
                RemoveTileAt(index);
            }
            InsertTile(index, placed);
        }
        UpdateEmptyHint();
        SaveRoster();
    }
```

3. After `OpenMachinesWindow`, add:

```csharp

    // "Dar acesso a um colega": one invite at a time, so asking again brings
    // the open one back.
    private void OpenGiveAccessWindow() =>
        ShowOnce("convite", Array.Empty<KnownMachine>(),
            () => new GiveAccessWindow(_masterCertificate, () => _tiles.Select(tile => tile.Machine).ToList()) { Owner = this });

    private void OpenJoinClassWindow() =>
        ShowOnce("entrar-na-sala", Array.Empty<KnownMachine>(),
            () => new JoinClassWindow(_masterCertificate, AddMachines) { Owner = this });

    private void OnJoinClassClicked(object sender, RoutedEventArgs e) => OpenJoinClassWindow();
```

In `src/Midview.Master/MainWindow.xaml`:

1. In `EmptyRosterPanel`, replace:

```xml
                <TextBlock FontSize="14" TextAlignment="Center" Foreground="{StaticResource InkMutedBrush}"
                           Text="Nenhuma máquina na sala ainda.&#x0a;Instale o Midview Aluno nas máquinas e procure-as na rede." />
                <Button Content="Procurar máquinas na rede" Click="OnSearchNetworkClicked"
                        HorizontalAlignment="Center" Margin="0,12,0,0" Padding="16,7" />
```

with:

```xml
                <TextBlock FontSize="14" TextAlignment="Center" Foreground="{StaticResource InkMutedBrush}"
                           Text="Nenhuma máquina na sala ainda.&#x0a;Instale o Midview Aluno nas máquinas e procure-as na rede,&#x0a;ou peça um código a um colega que já tem a sala." />
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,12,0,0">
                    <Button Content="Procurar máquinas na rede" Click="OnSearchNetworkClicked" Padding="16,7" />
                    <Button Content="Entrar na sala de um colega" Click="OnJoinClassClicked" Padding="16,7" Margin="8,0,0,0" />
                </StackPanel>
```

2. In the "Máquinas da sala" `ToolbarButton`, change the `ToolTip` to:

```
Procura e pareia as máquinas, atualiza o programa dos alunos, dá a sala a um colega ou entra na sala dele, permite outro professor e tem os ajustes avançados.
```

- [ ] **Step 5: Keep `MasterIdentity`'s comment true**

In `src/Midview.Master/MasterIdentity.cs`, append to the end of the class comment (just before `public static class MasterIdentity`), keeping every existing line:

```csharp
// Copying it still works, but "Dar acesso a um colega" (TeacherInvite) now
// gives a colleague the class under their own identity, with no file to
// copy.
```

- [ ] **Step 6: Build and run every test**

Run: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe build Midview.sln`
Expected: `0 Erro(s)`, only the 3 old `xUnit1031` warnings.

Run: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test Midview.sln`
Expected: PASS.

- [ ] **Step 7: Check the windows by hand**

Close any running "Midview - Sala de aula" (the Master runs one copy at a time). Then start the built one:

```bash
"src/Midview.Master/bin/Debug/net8.0-windows/Midview.Master.exe" &
```

On this one PC, check:
1. "Máquinas da sala" shows the link "Entrar na sala de um colega..." and the button "Dar acesso a um colega...". Nothing is cut off at the window's default size.
2. "Dar acesso a um colega..." with machines in the list shows a code like `K7P Q2M 94R` and "Vale por 10 minutos… Vai liberar as N máquinas da sua sala." Windows Firewall may ask about the program: a development build has no rule, the installed one will. Allow it on private networks for this test.
3. With that window open, "Entrar na sala de um colega...", then type the code in lower case with spaces. This PC has the same identity, so the answer must be "Este computador já usa a mesma identidade de professor que <este PC>; ele já tem acesso às mesmas máquinas." The first window must still show the code.
4. Type `K7P Q2M 94R` (a code for another certificate). The answer must be the "Nenhum professor na rede tem esse código aberto…" message, and the first window must still show its code.
5. Type `K7P Q2M 94U`. The answer must be "O código tem 9 letras e números, como K7P Q2M 94R." at once, without searching.
6. Close "Dar acesso a um colega". Reopen it: it shows a new code.

Write down anything that looks wrong and fix it before committing.

- [ ] **Step 8: Commit**

```bash
git add src/Midview.Master/GiveAccessWindow.xaml src/Midview.Master/GiveAccessWindow.xaml.cs src/Midview.Master/JoinClassWindow.xaml src/Midview.Master/JoinClassWindow.xaml.cs src/Midview.Master/MachinesWindow.xaml src/Midview.Master/MachinesWindow.xaml.cs src/Midview.Master/MainWindow.xaml src/Midview.Master/MainWindow.xaml.cs src/Midview.Master/MasterIdentity.cs
git commit -F - <<'EOF'
Let a teacher give a colleague the class, or take one, by invite code

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: The setup opens port 28902 for the teacher's program

**Files:**
- Modify: `installer/Midview.iss` (`[Code]`: constants, three new routines, `CurStepChanged`, `CurUninstallStepChanged`)

**Interfaces:**
- Consumes: `TeacherInvite.Port` = 28902 (Task 1), `EhProfessor`, `EhAluno`, `TipoInstalado`, `TipoProfessor`, `TipoAluno` (existing in the `.iss`).
- Produces: inbound rules `MidviewProfessorConvite` (TCP 28902) and `MidviewProfessorConviteBusca` (UDP 28902), limited to `{app}\Midview.Master.exe` and the local subnet. They are created by a "Professor" install and deleted by a "Professor" uninstall.

- [ ] **Step 1: Add the constants**

In `installer/Midview.iss`, in the `[Code]` `const` block, after `CodigoFalhaAoConfigurar = 10;`:

```pascal
  { "Dar acesso a um colega": the teacher's program listens here while an
    invite is open (TeacherInvite.Port). }
  PortaConvite = '28902';
  RegraConviteTcp = 'MidviewProfessorConvite';
  RegraConviteUdp = 'MidviewProfessorConviteBusca';
```

- [ ] **Step 2: Add the rule routines**

After the `RodarScript` function (and before `DescreverFalha`), add:

```pascal
function RodarNetsh(const Argumentos: String): Boolean;
var
  Codigo: Integer;
begin
  Codigo := -1;
  Result := Exec(ExpandConstant('{sys}\netsh.exe'), Argumentos, '', SW_HIDE, ewWaitUntilTerminated, Codigo) and (Codigo = 0);
  Log(Format('netsh %s: exit code=%d', [Argumentos, Codigo]));
end;

{ "add rule" never replaces a rule with the same name, so they are deleted
  first (deleting one that isn't there just fails, harmlessly). }
procedure ApagarRegrasDoConvite;
begin
  RodarNetsh('advfirewall firewall delete rule name="' + RegraConviteTcp + '"');
  RodarNetsh('advfirewall firewall delete rule name="' + RegraConviteUdp + '"');
end;

{ The Master runs without administrator rights, so it can't open its own
  port: the setup does, for this program and the local network only. }
function CriarRegrasDoConvite: Boolean;
var
  Restricao: String;
begin
  ApagarRegrasDoConvite;
  Restricao := ' dir=in action=allow localport=' + PortaConvite +
    ' program="' + ExpandConstant('{app}\Midview.Master.exe') + '" remoteip=localsubnet profile=any';
  Result := RodarNetsh('advfirewall firewall add rule name="' + RegraConviteTcp + '" protocol=TCP' + Restricao) and
    RodarNetsh('advfirewall firewall add rule name="' + RegraConviteUdp + '" protocol=UDP' + Restricao);
end;
```

- [ ] **Step 3: Create the rules on a "Professor" install**

Replace the start of `CurStepChanged`:

```pascal
{ Aluno: once the files are in place, set up and start the service. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  ArquivoDeErro: String;
  Codigo: Integer;
begin
  if (CurStep <> ssPostInstall) or not EhAluno then
    Exit;
```

with:

```pascal
{ Once the files are in place. Professor: open the invite's port. Aluno:
  set up and start the service. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  ArquivoDeErro: String;
  Codigo: Integer;
begin
  if CurStep <> ssPostInstall then
    Exit;
  if EhProfessor then begin
    if not CriarRegrasDoConvite then
      SuppressibleMsgBox('O Midview Professor foi instalado, mas a regra do firewall não pôde ser criada: ' +
        '"Dar acesso a um colega" não vai funcionar neste computador. Rode o instalador de novo.',
        mbError, MB_OK, IDOK);
    Exit;
  end;
  if not EhAluno then
    Exit;
```

(The rest of `CurStepChanged` stays as it is.)

- [ ] **Step 4: Delete the rules on a "Professor" uninstall**

Replace the start of `CurUninstallStepChanged`:

```pascal
  if (CurUninstallStep <> usUninstall) or (TipoInstalado <> TipoAluno) then
    Exit;
```

with:

```pascal
  if CurUninstallStep <> usUninstall then
    Exit;
  if TipoInstalado = TipoProfessor then begin
    ApagarRegrasDoConvite;
    Exit;
  end;
  if TipoInstalado <> TipoAluno then
    Exit;
```

Also change the comment above `CurUninstallStepChanged` from `{ Aluno: undo what a lesson left and remove the service, before the files go. }` to:

```pascal
{ Professor: close the invite's port. Aluno: undo what a lesson left and
  remove the service, before the files go. }
```

- [ ] **Step 5: Build the setup**

Run in PowerShell, with the working SDK first on PATH:

```powershell
$env:PATH = "C:\Users\Neto\AppData\Local\Microsoft\dotnet;" + $env:PATH; powershell -ExecutionPolicy Bypass -File installer\Gerar-Instalador.ps1 -Output "$env:TEMP\midview-setup-check"
```

Expected: the script ends without an error and `$env:TEMP\midview-setup-check\MidviewSetup.exe` exists. An ISCC error names the line: fix the Pascal and run again.

- [ ] **Step 6: Commit**

```bash
git add installer/Midview.iss
git commit -F - <<'EOF'
Open the invite port for the teacher's program on install

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 7: Mark the design implemented

**Files:**
- Modify: `docs/superpowers/specs/2026-10-14-teacher-invite-design.md` (the `Status:` line)

- [ ] **Step 1: Update the status**

Replace:

```
Status: Approved by the owner on 2026-10-14; not yet implemented
```

with (use the day it was finished):

```
Status: Implemented <yyyy-mm-dd> (not yet validated live)
```

- [ ] **Step 2: Run the whole suite one last time**

Run: `/c/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe test Midview.sln`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-10-14-teacher-invite-design.md
git commit -F - <<'EOF'
Mark giving a colleague the class as implemented

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

The live checks (two teacher PCs on the lab network with the firewall rules, a class of 25, one machine off and one on an old version, a wrong code, the window closed in the middle) need the lab and a release. They are the owner's to run, after the version is published through the usual release flow.
