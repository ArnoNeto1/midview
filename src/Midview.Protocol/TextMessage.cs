using System.Text;

namespace Midview.Protocol;

// The payload of ShowMessage and ChatText: plain UTF-8 text. The same rules
// apply on both ends -- the side typing limits what can be entered, the side
// receiving refuses anything else.
public static class TextMessage
{
    // Plenty for a classroom notice or a chat line, and still fits in one window.
    public const int MaxLength = 2000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] Encode(string text) => StrictUtf8.GetBytes(text);

    // Null when there's nothing worth showing: invalid UTF-8, only
    // whitespace, or longer than any sender lets through.
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
