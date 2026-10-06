using Midview.Protocol;

namespace Midview.Session;

// What the countdown banner says: the time left, and the warning for each
// thing that's coming (closing the student's programs, a shutdown or a
// restart), with what it says once the time is up.
public static class CountdownText
{
    private const string SaveNow = "Salve seu trabalho agora: o que não estiver salvo será perdido.";

    // "1:05". Rounded up, so it reads 0:00 only once the time is really up.
    public static string Clock(TimeSpan left)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(left.TotalSeconds));
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    public static (string Running, string Done) CloseApps(bool force) => (
        "O professor vai fechar seus programas. " + (force ? SaveNow : "Salve seu trabalho agora."),
        "O professor fechou seus programas.");

    public static (string Running, string Done) Power(PowerCountdown kind) => kind switch
    {
        PowerCountdown.Restart => ($"O professor vai reiniciar este computador. {SaveNow}", "Reiniciando..."),
        PowerCountdown.Scheduled => ($"Desligamento programado: este computador vai desligar. {SaveNow}", "Desligando..."),
        _ => ($"O professor vai desligar este computador. {SaveNow}", "Desligando..."),
    };
}
