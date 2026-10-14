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
