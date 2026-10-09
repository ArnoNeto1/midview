using System.Text;

namespace Midview.Protocol;

// The payload of MessageType.ClipboardText: the clipboard's text, UTF-8 and
// verbatim (no trimming -- leading and trailing spaces in a code snippet
// matter). Only text is synced, and only up to a size sensible to send over
// the control connection; anything larger isn't synced at all.
public static class ClipboardText
{
    public const int MaxBytes = 100_000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] Encode(string text) => StrictUtf8.GetBytes(text);

    // Encodes, or reports failure instead of throwing, for text that isn't
    // valid Unicode (an unpaired UTF-16 surrogate -- half of an emoji copied
    // by a tool that split it). Such text is skipped, never synced, so a
    // broken clipboard value can't crash the sender.
    public static bool TryEncode(string text, out byte[] bytes)
    {
        try
        {
            bytes = StrictUtf8.GetBytes(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            bytes = System.Array.Empty<byte>();
            return false;
        }
    }

    // Null when there is nothing to apply: empty, invalid UTF-8, or larger
    // than the cap (which the sending side already honors).
    public static string? Decode(byte[] payload)
    {
        if (payload.Length == 0 || payload.Length > MaxBytes)
        {
            return null;
        }
        try
        {
            return StrictUtf8.GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
