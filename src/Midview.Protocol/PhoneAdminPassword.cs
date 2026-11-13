using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Midview.Protocol;

// The phones' admin password ("senha de administrador dos celulares"): typed
// on a school phone, it frees the Wi-Fi and airplane mode for 10 minutes.
// The Master never sends the password itself, only a salted hash of it: the
// payload of MessageType.SetPhoneAdminPassword is a version byte (1), the
// PBKDF2-HMAC-SHA256 iterations (uint32, big-endian), a 16-byte salt and the
// 32-byte hash of the password's UTF-8 bytes; empty removes the password.
// The password is 6 to 12 ASCII letters or digits (never the 4-digit screen
// PIN). Same rules as PhoneAdminPassword.kt.
public static class PhoneAdminPassword
{
    public const int MinLength = 6;
    public const int MaxLength = 12;
    public const byte Version = 1;
    public const int SaltLength = 16;
    public const int HashLength = 32;
    public const int DefaultIterations = 100_000;

    // What a phone accepts: enough to make guessing slow, few enough that a
    // phone checks a password in about a second.
    public const int MinIterations = 10_000;
    public const int MaxIterations = 1_000_000;

    public const int PayloadLength = 1 + 4 + SaltLength + HashLength;

    // What the teacher reads for anything else (the agent says the same).
    public const string Invalid = "a senha tem que ter de 6 a 12 letras ou números";

    public sealed record Stored(int Iterations, byte[] Salt, byte[] Hash);

    public static bool IsValid(string? password) =>
        password is { Length: >= MinLength and <= MaxLength } && password.All(IsAllowed);

    public static bool IsAllowed(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    // A fresh random salt for each send.
    public static byte[] Encode(string password) =>
        Encode(password, RandomNumberGenerator.GetBytes(SaltLength), DefaultIterations);

    public static byte[] Encode(string password, byte[] salt, int iterations)
    {
        if (!IsValid(password))
        {
            throw new ArgumentException(Invalid, nameof(password));
        }
        if (salt.Length != SaltLength || iterations is < MinIterations or > MaxIterations)
        {
            throw new ArgumentException("salt or iterations out of range");
        }
        var payload = new byte[PayloadLength];
        payload[0] = Version;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(1, 4), iterations);
        salt.CopyTo(payload, 5);
        Hash(password, salt, iterations).CopyTo(payload, 5 + SaltLength);
        return payload;
    }

    public static byte[] Hash(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashLength);

    // Null for an empty payload (no password) or anything this version cannot use.
    public static Stored? Decode(byte[] payload)
    {
        if (payload.Length != PayloadLength || payload[0] != Version)
        {
            return null;
        }
        var iterations = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(1, 4));
        if (iterations is < MinIterations or > MaxIterations)
        {
            return null;
        }
        return new Stored(iterations, payload[5..(5 + SaltLength)], payload[(5 + SaltLength)..]);
    }

    public static bool Verify(string password, Stored stored) =>
        IsValid(password) && CryptographicOperations.FixedTimeEquals(Hash(password, stored.Salt, stored.Iterations), stored.Hash);
}
