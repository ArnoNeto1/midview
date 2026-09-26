using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PowerScheduleFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"midview-schedule-{Guid.NewGuid():N}");

    private string SchedulePath => Path.Combine(_directory, "power-schedule.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void NoFileYet_IsNoSchedule() => Assert.Null(PowerScheduleFile.Load(SchedulePath));

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var schedule = new PowerSchedule(1_790_000_000, PowerSchedule.Weekdays, 7 * 60, null);

        PowerScheduleFile.Save(SchedulePath, schedule);

        Assert.Equal(schedule, PowerScheduleFile.Load(SchedulePath));
    }

    [Fact]
    public void AnUnreadableFile_IsNoSchedule_AndIsKeptAside()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SchedulePath, "{ não é json");

        Assert.Null(PowerScheduleFile.Load(SchedulePath));
        Assert.True(File.Exists(SchedulePath + ".corrupt"));
    }

    [Fact]
    public void AnInvalidTime_IsNoSchedule()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SchedulePath, """{ "SavedAt": 1, "Days": 62, "WakeMinute": 5000, "ShutdownMinute": null }""");

        Assert.Null(PowerScheduleFile.Load(SchedulePath));
    }
}
