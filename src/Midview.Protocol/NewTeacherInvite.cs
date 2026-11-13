using System.Security.Cryptography;
using System.Text;

namespace Midview.Protocol;

// The code of "Permitir novo professor": teacher A's Master draws it, sends
// it to every machine of the room inside AllowNewTeacher, and shows it; A
// reads it out to teacher B, who types it while pairing ("Procurar e
// parear"). B's Master proves it knows the code in PairConfirm's payload
// (Proof), after the teacher compared the pairing codes, so a computer in
// the middle never gets a proof. A machine lets a teacher it doesn't trust
// in, while it trusts others, only with a valid proof and while the window
// is open; the first valid one uses the window up on that machine, and
// MaxTries pairings without one close it. Discovery says only that a window
// is open, never the code.
// The key is stretched (PBKDF2, Iterations) so a proof that did leak costs
// that much per guess; the thumbprints bind it to that machine and to B's
// certificate, the nonces to that pairing.
// Same bytes as src/Midview.Android/.../protocol/NewTeacherInvite.kt.
public static class NewTeacherInvite
{
    public const int CodeLength = 8;
    public const int ProofLength = 32;
    public const int Iterations = 100_000;

    // Pairings that bring a code to the student's screen without a valid
    // proof before the window closes.
    public const int MaxTries = 5;

    // How a machine that knows the code says its window is open: the old
    // ones say "liberada até 14:32" alone, and let anyone in.
    public const string WithCodeSuffix = " com o código";

    public const string BadCode = "O código tem 8 letras e números, como K7PQ 2M94.";

    private static readonly byte[] Label = Encoding.ASCII.GetBytes("Midview novo professor v1");

    public static string NewCode() =>
        new(Enumerable.Range(0, CodeLength)
            .Select(_ => TeacherInvite.Alphabet[RandomNumberGenerator.GetInt32(TeacherInvite.Alphabet.Length)])
            .ToArray());

    // What the teacher typed, as a code: case doesn't matter, spaces and
    // dashes are dropped, O is read as 0 and I or L as 1 (as TeacherInvite).
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
        var valid = IsCode(cleaned);
        code = valid ? cleaned : string.Empty;
        return valid;
    }

    public static bool IsCode(string code) => code.Length == CodeLength && code.All(c => TeacherInvite.Alphabet.Contains(c));

    // As A's window shows it: "K7PQ 2M94".
    public static string Format(string code) => $"{code[..4]} {code[4..]}";

    // AllowNewTeacher's payload; empty from a Master from before this.
    public static byte[] Encode(string code) => Encoding.ASCII.GetBytes(code);

    // The code in an AllowNewTeacher, or null (empty: an old Master).
    public static string? Decode(byte[] payload)
    {
        if (payload.Length != CodeLength)
        {
            return null;
        }
        var code = Encoding.ASCII.GetString(payload);
        return IsCode(code) ? code : null;
    }

    public static string WithCode(string untilText) => untilText + WithCodeSuffix;

    // A machine's answer to AllowNewTeacher says it asks for the code.
    public static bool AsksForCode(string detail) => detail.EndsWith(WithCodeSuffix, StringComparison.Ordinal);

    public static byte[] Key(string code, string agentThumbprint) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.ASCII.GetBytes(code), Label.Concat(Thumb(agentThumbprint)).ToArray(),
            Iterations, HashAlgorithmName.SHA256, ProofLength);

    public static byte[] Proof(byte[] key, string masterThumbprint, string agentThumbprint, byte[] agentNonce, byte[] masterNonce) =>
        HMACSHA256.HashData(
            key,
            Label.Concat(Thumb(masterThumbprint)).Concat(Thumb(agentThumbprint)).Concat(agentNonce).Concat(masterNonce).ToArray());

    public static bool Verifies(
        string code, byte[] proof, string masterThumbprint, string agentThumbprint, byte[] agentNonce, byte[] masterNonce) =>
        proof.Length == ProofLength
        && CryptographicOperations.FixedTimeEquals(
            proof, Proof(Key(code, agentThumbprint), masterThumbprint, agentThumbprint, agentNonce, masterNonce));

    private static byte[] Thumb(string thumbprint) => Encoding.ASCII.GetBytes(thumbprint.Trim().ToUpperInvariant());
}
