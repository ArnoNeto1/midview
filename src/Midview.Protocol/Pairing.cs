using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midview.Protocol;

// How a teacher and a student machine come to trust each other, over the
// Agent's TLS port, each side accepting a certificate it doesn't know yet:
//   Master -> PairRequest  the teacher's computer name, shown to the student
//   Agent  -> PairCommit   SHA-256 of the Agent's random nonce, or PairRefused (why, in Portuguese)
//   Master -> PairNonce    the Master's random nonce
//   Agent  -> PairReveal   the Agent's nonce; the code is on the student's screen now (or PairRefused)
//   Master -> PairConfirm or PairCancel, once the teacher compared the codes
//   Agent  -> PairDone     the machine, for the classroom list (PairingResult), or PairRefused
// Both ends compute the same 6-digit code from both certificates and both
// nonces. A computer in the middle holds two TLS sessions with different
// certificates, so the two screens show different codes; the commitment
// keeps it from steering them, since each nonce is fixed before the other
// one is known. The code protects the teacher: the student's side confirms
// nothing.
public static class Pairing
{
    public const int NonceLength = 32;
    public const int MaxTeacherNameLength = 40;

    // How long the teacher has to compare and confirm, from the reveal.
    public static readonly TimeSpan ConfirmTimeout = TimeSpan.FromMinutes(5);

    // How long "Permitir novo professor" lets one more teacher pair.
    public static readonly TimeSpan NewTeacherWindow = TimeSpan.FromMinutes(10);

    private static readonly byte[] Label = Encoding.ASCII.GetBytes("Midview pairing v1");

    public static byte[] NewNonce() => RandomNumberGenerator.GetBytes(NonceLength);

    public static byte[] Commit(byte[] nonce) => SHA256.HashData(nonce);

    public static bool Opens(byte[] commitment, byte[] nonce) =>
        nonce.Length == NonceLength && commitment.Length == SHA256.HashSizeInBytes
        && CryptographicOperations.FixedTimeEquals(commitment, Commit(nonce));

    public static int Code(string masterThumbprint, string agentThumbprint, byte[] agentNonce, byte[] masterNonce)
    {
        var input = Label
            .Concat(Encoding.ASCII.GetBytes(masterThumbprint.Trim().ToUpperInvariant()))
            .Concat(Encoding.ASCII.GetBytes(agentThumbprint.Trim().ToUpperInvariant()))
            .Concat(agentNonce)
            .Concat(masterNonce)
            .ToArray();
        var hash = SHA256.HashData(input);
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(hash) % 1_000_000);
    }

    public static string Format(int code) => $"{code / 1000:000} {code % 1000:000}";

    // The teacher's computer name as the student sees it: letters, digits,
    // '-', '_', '.' and spaces only (it also goes on a command line, see
    // PairingCodeArguments), never longer than MaxTeacherNameLength, and
    // "?" if nothing is left.
    public static string CleanTeacherName(string name)
    {
        var kept = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ').ToArray()).Trim();
        if (kept.Length > MaxTeacherNameLength)
        {
            kept = kept[..MaxTeacherNameLength].TrimEnd();
        }
        return kept.Length == 0 ? "?" : kept;
    }

    public static Message Refused(string reason) => new(MessageType.PairRefused, Encoding.UTF8.GetBytes(reason));

    public static string RefusalText(byte[] payload) => Encoding.UTF8.GetString(payload);
}

// What PairDone carries: the machine as the classroom list keeps it.
// MacAddress ("AA-BB-CC-DD-EE-FF") is the card the pairing came in on, for
// Wake-on-LAN; null if the Agent couldn't tell.
public sealed record PairingResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("session")] string SessionThumbprint,
    [property: JsonPropertyName("mac")] string? MacAddress)
{
    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static PairingResult? Decode(byte[] payload)
    {
        try
        {
            var result = JsonSerializer.Deserialize<PairingResult>(payload);
            return result is { Name.Length: > 0, SessionThumbprint.Length: > 0 } ? result : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
