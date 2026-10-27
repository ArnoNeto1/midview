namespace Midview.Protocol;

// The payload of MessageType.FillAdminPassword: the UTF-8 bytes of the admin
// password, nothing else. The whole payload is the password, so there is no
// length prefix or framing; the length is capped so a bad peer can't make the
// helper buffer or type an absurd amount.
public static class CredentialFill
{
    public const int MaxBytes = 512;

    // The Master validates the password before calling, so a bad length here is
    // a programming error, not input.
    public static byte[] Encode(ReadOnlySpan<byte> passwordUtf8)
    {
        if (passwordUtf8.Length is 0 or > MaxBytes)
            throw new ArgumentException($"The password must be 1 to {MaxBytes} bytes.", nameof(passwordUtf8));
        return passwordUtf8.ToArray();
    }

    // Returns a fresh array, never the incoming buffer.
    public static bool TryDecode(byte[] payload, out byte[] passwordUtf8)
    {
        if (payload.Length is 0 or > MaxBytes)
        {
            passwordUtf8 = Array.Empty<byte>();
            return false;
        }
        passwordUtf8 = (byte[])payload.Clone();
        return true;
    }
}
