using Midview.Protocol;
using Xunit;

public class PowerScheduleTests
{
    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        var schedule = new PowerSchedule(1_790_000_000, PowerSchedule.Weekdays, 7 * 60, 18 * 60);

        Assert.True(PowerSchedule.TryDecode(schedule.Encode(), out var decoded));
        Assert.Equal(schedule, decoded);
    }

    [Fact]
    public void EncodeThenDecode_NothingScheduled_StaysNull()
    {
        var schedule = new PowerSchedule(1, PowerSchedule.EveryDay, null, null);

        Assert.True(PowerSchedule.TryDecode(schedule.Encode(), out var decoded));
        Assert.Null(decoded.WakeMinute);
        Assert.Null(decoded.ShutdownMinute);
    }

    [Fact]
    public void Decode_WrongLength_IsRefused() =>
        Assert.False(PowerSchedule.TryDecode(new byte[PowerSchedule.PayloadLength - 1], out _));

    [Fact]
    public void Decode_ATimePastMidnight_IsRefused()
    {
        var payload = new PowerSchedule(1, PowerSchedule.Weekdays, null, 18 * 60).Encode();
        payload[11] = 0x05; // 0x05A0 = 1440 minutes = 24:00
        payload[12] = 0xA0;

        Assert.False(PowerSchedule.TryDecode(payload, out _));
    }

    [Fact]
    public void Decode_AnEighthDay_IsRefused()
    {
        var payload = new PowerSchedule(1, PowerSchedule.Weekdays, null, 18 * 60).Encode();
        payload[8] = 0x80;

        Assert.False(PowerSchedule.TryDecode(payload, out _));
    }

    [Fact]
    public void IsScheduledOn_FollowsDayOfWeek()
    {
        var weekdays = new PowerSchedule(1, PowerSchedule.Weekdays, null, null);

        Assert.True(weekdays.IsScheduledOn(DayOfWeek.Monday));
        Assert.True(weekdays.IsScheduledOn(DayOfWeek.Friday));
        Assert.False(weekdays.IsScheduledOn(DayOfWeek.Saturday));
        Assert.False(weekdays.IsScheduledOn(DayOfWeek.Sunday));
    }

    [Theory]
    [InlineData("7", 7 * 60)]
    [InlineData("7:00", 7 * 60)]
    [InlineData(" 07:30 ", 7 * 60 + 30)]
    [InlineData("18h", 18 * 60)]
    [InlineData("18h45", 18 * 60 + 45)]
    [InlineData("0:00", 0)]
    [InlineData("23:59", 23 * 60 + 59)]
    public void ParseTime_AcceptsTheUsualWritings(string text, int expected)
    {
        Assert.True(PowerSchedule.TryParseTime(text, out var minute));
        Assert.Equal(expected, minute);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("24:00")]
    [InlineData("7:60")]
    [InlineData("7:5")]
    [InlineData("sete")]
    public void ParseTime_RefusesTheRest(string? text) =>
        Assert.False(PowerSchedule.TryParseTime(text, out _));

    [Fact]
    public void FormatTime_IsHoursAndMinutes() =>
        Assert.Equal("07:05", PowerSchedule.FormatTime(7 * 60 + 5));

    [Theory]
    [InlineData(PowerSchedule.Weekdays, "seg a sex")]
    [InlineData(PowerSchedule.EveryDay, "todos os dias")]
    [InlineData(0, "nenhum dia")]
    [InlineData(0b0000010, "seg")]
    [InlineData(0b1000001, "sáb e dom")]
    [InlineData(0b0101010, "seg, qua e sex")]
    [InlineData(0b0001110, "seg a qua")]
    public void DescribeDays_ReadsLikeASchoolWeek(byte days, string expected) =>
        Assert.Equal(expected, PowerSchedule.DescribeDays(days));
}
