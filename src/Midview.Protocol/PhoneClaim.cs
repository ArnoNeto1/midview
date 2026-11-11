using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midview.Protocol;

// How a school phone prepared by a QR code joins the teacher's class on its
// own. The QR carries a secret token; the phone never says the token aloud
// while it is still searching, only its ClaimId (a fingerprint of the token)
// in the discovery reply. The Master that made the QR knows the token behind
// each ClaimId it is waiting for, so it can tell "my phone" from any other
// device on the network, then sends ClaimPhone with the token as proof.
public static class PhoneClaim
{
    // 32 random bytes in URL-safe Base64 without padding: 43 characters.
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // The first 16 hex characters (upper case) of SHA-256 of the token's
    // UTF-8 bytes. Short enough for a discovery reply, and it doesn't give
    // the token away.
    public static string ClaimId(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..16];
}

// What ClaimPhone carries: the proof from the QR and the teacher's computer
// name, shown on the phone. Sent over the Android agent's TLS port.
public sealed record PhoneClaimRequest(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("teacher")] string TeacherName)
{
    // Nothing legitimate comes close to this, so a bigger payload is junk.
    public const int MaxPayloadLength = 4096;

    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static PhoneClaimRequest? Decode(byte[] payload)
    {
        if (payload.Length > MaxPayloadLength)
        {
            return null;
        }

        try
        {
            var request = JsonSerializer.Deserialize<PhoneClaimRequest>(payload);
            return request is { Token.Length: > 0, TeacherName: not null } ? request : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
