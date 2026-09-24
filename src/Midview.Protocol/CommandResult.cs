using System.Text;

namespace Midview.Protocol;

// A CommandAck payload that says more than yes/no: 1 or 0, then UTF-8 detail
// for the teacher (a saved file's name, how many programs were closed, or why
// something failed).
public static class CommandResult
{
    public static byte[] Encode(bool succeeded, string detail)
    {
        var text = Encoding.UTF8.GetBytes(detail);
        var result = new byte[1 + text.Length];
        result[0] = (byte)(succeeded ? 1 : 0);
        text.CopyTo(result, 1);
        return result;
    }

    public static (bool Succeeded, string Detail) Decode(byte[] payload) =>
        payload.Length == 0
            ? (false, string.Empty)
            : (payload[0] == 1, Encoding.UTF8.GetString(payload, 1, payload.Length - 1));
}
