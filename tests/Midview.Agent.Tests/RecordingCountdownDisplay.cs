using Midview.Agent;
using Midview.Protocol;

// What the Agent asked to show on the student's screen, and how often it
// took it down.
public sealed class RecordingCountdownDisplay : ICountdownDisplay
{
    public List<(PowerCountdown Kind, int Seconds)> Shown { get; } = new();

    public int Closed { get; private set; }

    public void Show(PowerCountdown kind, int seconds) => Shown.Add((kind, seconds));

    public void Close() => Closed++;
}
