using Midview.Protocol;
using Xunit;

public class CountdownArgumentsTests
{
    [Theory]
    [InlineData(PowerCountdown.Shutdown, 60, "--contagem desligar 60")]
    [InlineData(PowerCountdown.Restart, 300, "--contagem reiniciar 300")]
    [InlineData(PowerCountdown.Scheduled, 300, "--contagem programado 300")]
    public void Build_ThenParse(PowerCountdown kind, int seconds, string expected)
    {
        Assert.Equal(expected, CountdownArguments.Build(kind, seconds));
        Assert.Equal((kind, seconds), CountdownArguments.Parse(expected.Split(' ')));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--contagem desligar")]
    [InlineData("--contagem explodir 60")]
    [InlineData("--contagem desligar 0")]
    [InlineData("--contagem desligar -5")]
    [InlineData("--contagem desligar 99999")]
    [InlineData("--contagem desligar sessenta")]
    [InlineData("--pairing-code 123456 --from PROF")]
    public void Parse_RefusesAnythingElse(string arguments)
    {
        Assert.Null(CountdownArguments.Parse(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
    }
}
