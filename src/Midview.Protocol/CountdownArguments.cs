namespace Midview.Protocol;

// What the student is counted down to by a shutdown the Agent started.
public enum PowerCountdown
{
    Shutdown,
    Restart,
    Scheduled,
}

// The Session's command line for the countdown banner the Agent puts on the
// student's screen while a shutdown or restart is on its way
// (Midview.Session.exe --contagem desligar 60). The Agent runs as SYSTEM
// and can't show a window itself; the Session can, as the student.
public static class CountdownArguments
{
    public const string Flag = "--contagem";

    // Longer than any delay a shutdown is given (PowerCommand, PowerSchedule).
    public const int MaxSeconds = 60 * 60;

    public static string Build(PowerCountdown kind, int seconds) => $"{Flag} {Name(kind)} {seconds}";

    public static (PowerCountdown Kind, int Seconds)? Parse(string[] args) =>
        args is [Flag, var name, var count] && KindOf(name) is { } kind
        && int.TryParse(count, out var seconds) && seconds is > 0 and <= MaxSeconds
            ? (kind, seconds)
            : null;

    private static string Name(PowerCountdown kind) => kind switch
    {
        PowerCountdown.Restart => "reiniciar",
        PowerCountdown.Scheduled => "programado",
        _ => "desligar",
    };

    private static PowerCountdown? KindOf(string name) => name switch
    {
        "desligar" => PowerCountdown.Shutdown,
        "reiniciar" => PowerCountdown.Restart,
        "programado" => PowerCountdown.Scheduled,
        _ => null,
    };
}
