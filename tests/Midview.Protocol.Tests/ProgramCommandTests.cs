using Midview.Protocol;
using Xunit;

public class ProgramCommandTests
{
    [Theory]
    [InlineData("powershell", "powershell", "")]
    [InlineData("  notepad   C:\\aula\\texto.txt  ", "notepad", "C:\\aula\\texto.txt")]
    [InlineData("\"C:\\Program Files\\Programa\\app.exe\" -a \"b c\"", "C:\\Program Files\\Programa\\app.exe", "-a \"b c\"")]
    [InlineData("\"C:\\Program Files\\Programa\\app.exe\"", "C:\\Program Files\\Programa\\app.exe", "")]
    [InlineData("%USERPROFILE%\\Desktop\\aula.pptx", "%USERPROFILE%\\Desktop\\aula.pptx", "")]
    [InlineData("chrome https://www.google.com", "chrome", "https://www.google.com")]
    public void TryParse_SplitsTheProgramFromItsArguments(string typed, string program, string arguments)
    {
        Assert.True(ProgramCommand.TryParse(typed, out var parsedProgram, out var parsedArguments, out var problem));
        Assert.Equal(program, parsedProgram);
        Assert.Equal(arguments, parsedArguments);
        Assert.Equal(string.Empty, problem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_Nothing_AsksForTheCommand(string? typed)
    {
        Assert.False(ProgramCommand.TryParse(typed, out _, out _, out var problem));
        Assert.StartsWith("Escreva o comando", problem);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Programa\\app.exe -a")]
    [InlineData("\"\" -a")]
    public void TryParse_AQuotedProgramMustBeClosedAndNotEmpty(string typed)
    {
        Assert.False(ProgramCommand.TryParse(typed, out _, out _, out var problem));
        Assert.Contains("aspas", problem);
    }

    [Fact]
    public void TryParseLines_OneProgramPerLine_SkippingBlankOnes()
    {
        Assert.True(ProgramCommand.TryParseLines("powershell\r\n\n  notepad aula.txt  \n", out var commands, out var problem));
        Assert.Equal(new[] { "powershell", "notepad aula.txt" }, commands);
        Assert.Equal(string.Empty, problem);
    }

    [Fact]
    public void TryParseLines_SaysWhichLineIsWrong()
    {
        Assert.False(ProgramCommand.TryParseLines("powershell\n\n\"sem fim", out _, out var problem));
        Assert.StartsWith("Linha 3: ", problem);
    }

    [Fact]
    public void TryParseLines_OneWrongLine_NeedsNoNumber()
    {
        Assert.False(ProgramCommand.TryParseLines("\"sem fim", out _, out var problem));
        Assert.Contains("aspas", problem);
        Assert.DoesNotContain("Linha", problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n  \n")]
    public void TryParseLines_Nothing_AsksForTheCommand(string typed)
    {
        Assert.False(ProgramCommand.TryParseLines(typed, out _, out var problem));
        Assert.StartsWith("Escreva o comando", problem);
    }

    [Fact]
    public void TryParse_TooLong_IsRefused()
    {
        Assert.False(ProgramCommand.TryParse("notepad " + new string('x', TextMessage.MaxLength), out _, out _, out var problem));
        Assert.Equal("Comando longo demais.", problem);
    }
}
