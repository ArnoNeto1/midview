using Midview.Protocol;

namespace Midview.Master;

// A stretch of the day as fractions of it, 0 (midnight) to 1 (next midnight).
public sealed record TimelineSegment(double Start, double End);

// What the schedule window's day ruler shows: the stretches the lab is on and
// the sentence that says it. Pure, so it can be tested without a window.
public static class ScheduleTimeline
{
    private const double MinutesPerDay = 1440.0;

    public static (IReadOnlyList<TimelineSegment> Segments, string Text) For(int? wakeMinute, int? shutdownMinute)
    {
        switch (wakeMinute, shutdownMinute)
        {
            case (null, null):
                return (Array.Empty<TimelineSegment>(), "Nada agendado");
            case (int wake, null):
                return (new[] { new TimelineSegment(wake / MinutesPerDay, 1) },
                    $"Liga às {PowerSchedule.FormatTime(wake)} e fica ligado até alguém desligar");
            case (null, int shutdown):
                return (Array.Empty<TimelineSegment>(), $"Desliga às {PowerSchedule.FormatTime(shutdown)}");
            case (int wake, int shutdown) when wake == shutdown:
                return (Array.Empty<TimelineSegment>(), "Os horários de ligar e de desligar são iguais.");
            case (int wake, int shutdown) when wake < shutdown:
                return (new[] { new TimelineSegment(wake / MinutesPerDay, shutdown / MinutesPerDay) },
                    $"Ligado das {PowerSchedule.FormatTime(wake)} às {PowerSchedule.FormatTime(shutdown)}");
            case (int wake, int shutdown):
                return (new[]
                    {
                        new TimelineSegment(0, shutdown / MinutesPerDay),
                        new TimelineSegment(wake / MinutesPerDay, 1),
                    },
                    $"Ligado das {PowerSchedule.FormatTime(wake)} às {PowerSchedule.FormatTime(shutdown)} do dia seguinte");
        }
    }
}
