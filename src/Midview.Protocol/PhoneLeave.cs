using System.Text;

namespace Midview.Protocol;

// "Desfazer e tirar o Midview" (MessageType.RemoveMidviewFromPhone): the
// payload is the word the teacher typed in the Master to confirm, "REMOVER",
// in UTF-8. The agent acts only on exactly that word, so a stray or
// half-sent message can never take the Midview off a phone. Same rules as
// PhoneLeaveRequest.kt.
public static class PhoneLeave
{
    // What the teacher types to confirm "Desfazer e tirar o Midview".
    public const string Word = "REMOVER";

    // What the teacher types to confirm "Restaurar de fábrica" (WipePhone,
    // which carries no payload: the word stays in the Master).
    public const string WipeWord = "APAGAR";

    public static byte[] Encode() => Encoding.UTF8.GetBytes(Word);

    public static bool IsConfirmed(byte[] payload) => payload.AsSpan().SequenceEqual(Encode());
}
