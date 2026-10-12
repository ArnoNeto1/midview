using System.Text;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class TypedTextTests
{
    public static TheoryData<string, int> Texts => new()
    {
        { "Bom dia, turma!", -1 },
        { "", -1 },
        // A whole emoji is a surrogate pair: fine.
        { "Parabéns 🎉", -1 },
        // Half an emoji, cut by the app it was pasted from.
        { "olá \ud83c", 4 },
        { "\ud83c olá", 0 },
        { "a\udf89b", 1 },
        { "🎉\ud83c", 2 },
        // A pair the wrong way round is two halves.
        { "\udf89\ud83c", 0 },
    };

    [Theory]
    [MemberData(nameof(Texts))]
    public void IndexOfInvalidCharacter_FindsWhatTextMessageCantEncode(string text, int expected)
    {
        Assert.Equal(expected, TypedText.IndexOfInvalidCharacter(text));

        if (expected < 0)
        {
            TextMessage.Encode(text);
        }
        else
        {
            Assert.Throws<EncoderFallbackException>(() => TextMessage.Encode(text));
        }
    }
}
