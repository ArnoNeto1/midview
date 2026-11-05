using Midview.Master;
using Xunit;

// The day ruler of the schedule window: which stretches of the day the lab is
// on, and the sentence that says it.
public sealed class ScheduleTimelineTests
{
    private const double Tolerance = 1e-9;

    private static void AssertSegment(TimelineSegment segment, double start, double end)
    {
        Assert.InRange(segment.Start, start - Tolerance, start + Tolerance);
        Assert.InRange(segment.End, end - Tolerance, end + Tolerance);
    }

    [Fact]
    public void BothInOrder()
    {
        var (segments, text) = ScheduleTimeline.For(420, 1080);
        AssertSegment(Assert.Single(segments), 420 / 1440.0, 0.75);
        Assert.Equal("Ligado das 07:00 às 18:00", text);
    }

    [Fact]
    public void PastMidnight()
    {
        var (segments, text) = ScheduleTimeline.For(1320, 360);
        Assert.Equal(2, segments.Count);
        AssertSegment(segments[0], 0, 0.25);
        AssertSegment(segments[1], 1320 / 1440.0, 1);
        Assert.Equal("Ligado das 22:00 às 06:00 do dia seguinte", text);
    }

    [Fact]
    public void WakeOnly()
    {
        var (segments, text) = ScheduleTimeline.For(420, null);
        AssertSegment(Assert.Single(segments), 420 / 1440.0, 1);
        Assert.Equal("Liga às 07:00 e fica ligado até alguém desligar", text);
    }

    [Fact]
    public void ShutdownOnly()
    {
        var (segments, text) = ScheduleTimeline.For(null, 1080);
        Assert.Empty(segments);
        Assert.Equal("Desliga às 18:00", text);
    }

    [Fact]
    public void Nothing()
    {
        var (segments, text) = ScheduleTimeline.For(null, null);
        Assert.Empty(segments);
        Assert.Equal("Nada agendado", text);
    }

    [Fact]
    public void SameTime()
    {
        var (segments, text) = ScheduleTimeline.For(420, 420);
        Assert.Empty(segments);
        Assert.Equal("Os horários de ligar e de desligar são iguais.", text);
    }
}
