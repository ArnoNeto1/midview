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

    public static TheoryData<string, int> CommandsWithHalfAnEmoji => new()
    {
        { "notepad olá \ud83c", 12 },
        { "\"C:\\Aula \ud83c\\app.exe\"", 9 },
        { "powershell\nnotepad \udf89.txt", 19 },
        // The window's text box ends lines with \r\n.
        { "powershell\r\nnotepad \udf89", 20 },
        // Said before what's wrong on another line: it's the one that
        // closed the Master.
        { "\"sem fim\nnotepad \ud83c", 17 },
    };

    // ProgramCommand lets it through, and TextMessage.Encode would throw for
    // it when sending.
    [Theory]
    [MemberData(nameof(CommandsWithHalfAnEmoji))]
    public void TryParseCommands_HalfAnEmoji_IsAProblem_AndSaysWhere(string typed, int expected)
    {
        Assert.False(TypedText.TryParseCommands(typed, out var commands, out var problem, out var invalidAt));

        Assert.Equal(expected, invalidAt);
        Assert.Equal("O comando tem um caractere inválido (talvez metade de um emoji colado).", problem);
        Assert.Empty(commands);
    }

    [Fact]
    public void TryParseCommands_WholeEmoji_AreFine()
    {
        Assert.True(TypedText.TryParseCommands("notepad Parabéns🎉.txt\r\n\r\n  powershell  ", out var commands, out var problem, out var invalidAt));

        Assert.Equal(new[] { "notepad Parabéns🎉.txt", "powershell" }, commands);
        Assert.Equal(string.Empty, problem);
        Assert.Equal(-1, invalidAt);
        foreach (var command in commands)
        {
            TextMessage.Encode(command);
        }
    }

    [Fact]
    public void TryParseCommands_OtherProblems_AreProgramCommands()
    {
        Assert.False(ProgramCommand.TryParseLines("powershell\n\n\"sem fim", out _, out var expected));

        Assert.False(TypedText.TryParseCommands("powershell\n\n\"sem fim", out _, out var problem, out var invalidAt));

        Assert.Equal(expected, problem);
        Assert.Equal(-1, invalidAt);
    }
}
