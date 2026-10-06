using Midview.Protocol;
using Midview.Session;
using Xunit;

public class CountdownTextTests
{
    // Rounded up, so it shows 0:00 only once the time is really up.
    [Theory]
    [InlineData(65.0, "1:05")]
    [InlineData(60.0, "1:00")]
    [InlineData(59.2, "1:00")]
    [InlineData(9.0, "0:09")]
    [InlineData(0.4, "0:01")]
    [InlineData(0.0, "0:00")]
    [InlineData(-3.0, "0:00")]
    [InlineData(300.0, "5:00")]
    public void Clock_ShowsMinutesAndSeconds(double seconds, string expected)
    {
        Assert.Equal(expected, CountdownText.Clock(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ClosingPrograms_SaysToSave_AndWhatHappened()
    {
        Assert.Equal(
            ("O professor vai fechar seus programas. Salve seu trabalho agora.", "O professor fechou seus programas."),
            CountdownText.CloseApps(force: false));
        Assert.Equal(
            ("O professor vai fechar seus programas. Salve seu trabalho agora: o que não estiver salvo será perdido.",
             "O professor fechou seus programas."),
            CountdownText.CloseApps(force: true));
    }

    [Theory]
    [InlineData(PowerCountdown.Shutdown, "O professor vai desligar este computador. Salve seu trabalho agora: o que não estiver salvo será perdido.", "Desligando...")]
    [InlineData(PowerCountdown.Restart, "O professor vai reiniciar este computador. Salve seu trabalho agora: o que não estiver salvo será perdido.", "Reiniciando...")]
    [InlineData(PowerCountdown.Scheduled, "Desligamento programado: este computador vai desligar. Salve seu trabalho agora: o que não estiver salvo será perdido.", "Desligando...")]
    public void Shutdowns_SayWhatIsComing(PowerCountdown kind, string running, string done)
    {
        Assert.Equal((running, done), CountdownText.Power(kind));
    }
}
