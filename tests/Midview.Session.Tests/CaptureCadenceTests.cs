using Midview.Session;
using Xunit;

public class CaptureCadenceTests
{
    [Fact]
    public void StillScreen_KeepsTheOldPace()
    {
        var cadence = new CaptureCadence();

        Assert.Equal(CaptureCadence.IdleIntervalMs - 20, cadence.RestMs(20, 20, 10_000));
    }

    [Fact]
    public void TeacherInput_CapturesTwiceAsOften_ForASecond()
    {
        var cadence = new CaptureCadence();
        cadence.Activity(10_000);

        Assert.Equal(CaptureCadence.ActiveIntervalMs - 20, cadence.RestMs(20, 20, 10_500));
        Assert.Equal(CaptureCadence.IdleIntervalMs - 20, cadence.RestMs(20, 20, 11_000));
    }

    [Fact]
    public void ChangesOnTwoCapturesInARow_CountAsMoving()
    {
        var cadence = new CaptureCadence();
        cadence.Captured(true, 10_000);
        cadence.Captured(true, 10_066);

        Assert.Equal(CaptureCadence.ActiveIntervalMs - 10, cadence.RestMs(10, 10, 10_100));
    }

    [Fact]
    public void ABlinkingCaret_DoesNotCountAsMoving()
    {
        var cadence = new CaptureCadence();
        for (var capture = 0; capture < 20; capture++)
        {
            cadence.Captured(capture % 8 == 0, 10_000 + capture * 66);
        }

        Assert.Equal(CaptureCadence.IdleIntervalMs - 10, cadence.RestMs(10, 10, 11_400));
    }

    [Fact]
    public void SlowCapture_StillRestsHalfAsLongAsItWorked()
    {
        var cadence = new CaptureCadence();
        cadence.Activity(10_000);

        Assert.Equal(25, cadence.RestMs(50, 50, 10_100));
        Assert.Equal(40, cadence.RestMs(80, 80, 20_000));
    }

    [Fact]
    public void WaitingOnTheNetwork_IsNotCountedAsWork()
    {
        var cadence = new CaptureCadence();
        cadence.Activity(10_000);

        // 25 ms of capturing and encoding, then 50 ms for a slow link to take
        // the update: the rest is half the work, not half of all 75 ms.
        Assert.Equal(12, cadence.RestMs(25, 75, 10_100));
    }
}
