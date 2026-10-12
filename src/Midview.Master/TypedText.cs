using Midview.Protocol;

namespace Midview.Master;

// Text the teacher typed or pasted for the students. It goes out as a
// TextMessage, whose strict UTF-8 can't encode an unpaired UTF-16
// surrogate -- half of an emoji, cut by the app it was copied from -- and
// TextMessage.Encode throws for one. SendMessageWindow and "Abrir programa"
// look for it first, so they can point the teacher at it instead of sending.
public static class TypedText
{
    // Where the first unpaired surrogate is, or -1 if there's none.
    public static int IndexOfInvalidCharacter(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return i;
            }
        }
        return -1;
    }

    // "Abrir programa", typed in the window or saved in the menu: the
    // commands as ProgramCommand splits them, which lets that character
    // through. It's looked for first; invalidAt is where it is in typed, for
    // the window to select it, and -1 for any other problem.
    public static bool TryParseCommands(string typed, out IReadOnlyList<string> commands, out string problem, out int invalidAt)
    {
        invalidAt = IndexOfInvalidCharacter(typed);
        if (invalidAt >= 0)
        {
            commands = Array.Empty<string>();
            problem = "O comando tem um caractere inválido (talvez metade de um emoji colado).";
            return false;
        }
        return ProgramCommand.TryParseLines(typed, out commands, out problem);
    }
}
