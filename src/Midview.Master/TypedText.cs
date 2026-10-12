namespace Midview.Master;

// Text the teacher typed or pasted for the students. It goes out as a
// TextMessage, whose strict UTF-8 can't encode an unpaired UTF-16
// surrogate -- half of an emoji, cut by the app it was copied from -- and
// TextMessage.Encode throws for one. SendMessageWindow looks for it first,
// so it can point the teacher at it instead of sending.
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
}
