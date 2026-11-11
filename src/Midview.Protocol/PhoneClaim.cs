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
    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    // Same shape as a token: a fresh value the Master picks for each claim.
    public static string NewNonce() => Base64Url(RandomNumberGenerator.GetBytes(32));

    // What the phone answers with, so the Master knows it holds the token
    // without the token ever going on the wire: HMAC-SHA256 keyed by the
    // token over the Master's nonce and both thumbprints (upper case),
    // in URL-safe Base64 without padding.
    public static string Proof(string token, string nonce, string agentThumbprint, string masterThumbprint) =>
        Base64Url(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(nonce + agentThumbprint.Trim().ToUpperInvariant() + masterThumbprint.Trim().ToUpperInvariant())));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // The first 16 hex characters (upper case) of SHA-256 of the token's
    // UTF-8 bytes. Short enough for a discovery reply, and it doesn't give
    // the token away.
    public static string ClaimId(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..16];
}

// What ClaimPhone carries: a fresh nonce (the phone proves it knows the QR's
// token over it, see PhoneClaim.Proof) and the teacher's computer name,
// shown on the phone. The token itself is never sent. Sent over the Android agent's TLS port.
public sealed record PhoneClaimRequest(
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("teacher")] string TeacherName)
{
    // Nothing legitimate comes close to this, so a bigger payload is junk.
    public const int MaxPayloadLength = 4096;

    private static bool IsNonce(string value) =>
        value.Length == 43 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

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
            return request is { Nonce: { } nonce, TeacherName: not null } && IsNonce(nonce) ? request : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
