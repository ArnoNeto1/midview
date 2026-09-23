using System.Text;

namespace Midview.Protocol;

// The payload of a ShowMessage: plain UTF-8 text, shared by the Master
// (which limits what the teacher can type) and the Session (which refuses
// anything else).
public static class TeacherMessage
{
    // Plenty for a classroom notice, and still fits in one window.
    public const int MaxLength = 2000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] Encode(string text) => StrictUtf8.GetBytes(text);

    // Null when there's nothing worth showing: invalid UTF-8, only
    // whitespace, or longer than any message the Master lets through.
    public static string? Decode(byte[] payload)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(payload).Trim();
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        return text.Length is > 0 and <= MaxLength ? text : null;
    }
}
