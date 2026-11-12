using Midview.Master;
using Xunit;

public class AddressSearchScheduleTests
{
    private static readonly DateTime Start = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);
    private const string Phone = "AAAA";
    private const string Pc = "BBBB";

    private static DateTime At(double seconds) => Start.AddSeconds(seconds);

    // Called every few seconds, as MainWindow's timer does, from..to; the
    // first time it said yes, or null.
    private static DateTime? FirstDue(AddressSearchSchedule schedule, double from, double to, params string[] offline)
    {
        for (var t = from; t <= to; t += 5)
        {
            if (schedule.Due(At(t), offline))
            {
                return At(t);
            }
        }
        return null;
    }

    [Fact]
    public void NoMachineOffline_NeverSearches()
    {
        var schedule = new AddressSearchSchedule(Start);

        Assert.Null(FirstDue(schedule, 0, 3600));
    }

    // The phone on another Wi-Fi: offline from the first poll, found by the
    // first search, about 45 s after the Master opened.
    [Fact]
    public void AMachineOfflineFromTheStart_IsLookedFor_AfterTheFirstPollsHadTheirTime()
    {
        var schedule = new AddressSearchSchedule(Start);

        Assert.Equal(At(45), FirstDue(schedule, 5, 600, Phone));
    }

    // A machine that just stopped answering may be restarting: not before
    // it has been offline for OfflineFor.
    [Fact]
    public void AMachineThatJustWentOffline_IsLookedFor_OnlyAfterAWhile()
    {
        var schedule = new AddressSearchSchedule(Start);
        Assert.Null(FirstDue(schedule, 0, 300));

        Assert.Equal(At(335), FirstDue(schedule, 305, 900, Pc));
    }

    // Back online before OfflineFor (a poll that failed once): no search,
    // and the time offline starts over the next time.
    [Fact]
    public void AMachineBackOnline_StopsTheWait()
    {
        var schedule = new AddressSearchSchedule(Start);
        Assert.Null(FirstDue(schedule, 60, 80, Pc));
        Assert.False(schedule.Due(At(85), Array.Empty<string>()));

        Assert.Equal(At(120), FirstDue(schedule, 90, 600, Pc));
    }

    [Fact]
    public void AfterASearch_WaitsTheInterval()
    {
        var schedule = new AddressSearchSchedule(Start);
        Assert.Equal(At(45), FirstDue(schedule, 5, 600, Phone));
        schedule.Searched(At(47), moved: 0);

        Assert.Equal(At(47 + 180 + 3), FirstDue(schedule, 50, 1000, Phone));
    }

    // PCs simply switched off stay offline all class long: searches that
    // find nothing for them come less and less often, up to LongestInterval.
    [Fact]
    public void FruitlessSearches_ForTheSameMachines_WaitLonger_UpToTheLongest()
    {
        var schedule = new AddressSearchSchedule(Start);
        var t = 0.0;
        var gaps = new List<double>();
        DateTime? last = null;
        for (var i = 0; i < 6; i++)
        {
            var due = FirstDue(schedule, t, t + 3600, Pc)!.Value;
            if (last is { } previous)
            {
                gaps.Add((due - previous).TotalMinutes);
            }
            schedule.Searched(due, moved: 0);
            last = due;
            t = (due - Start).TotalSeconds + 5;
        }

        Assert.Equal(new[] { 3.0, 6.0, 12.0, 12.0, 12.0 }, gaps.Select(gap => Math.Round(gap)).ToArray());
    }

    // Another machine going offline is looked for at the usual interval,
    // not after the longer wait of the ones already looked for.
    [Fact]
    public void AMachineNewlyOffline_BringsTheIntervalBack()
    {
        var schedule = new AddressSearchSchedule(Start);
        Assert.Equal(At(45), FirstDue(schedule, 5, 600, Pc));
        schedule.Searched(At(45), moved: 0);
        Assert.Equal(At(225), FirstDue(schedule, 50, 1000, Pc));
        schedule.Searched(At(225), moved: 0); // the next one for the PC alone: 6 min later
        Assert.Null(FirstDue(schedule, 230, 400, Pc));

        Assert.Equal(At(405 + 30), FirstDue(schedule, 405, 1000, Pc, Phone));
    }

    [Fact]
    public void ASearchThatMovedAMachine_BringsTheIntervalBack()
    {
        var schedule = new AddressSearchSchedule(Start);
        schedule.Due(At(0), new[] { Pc });
        schedule.Searched(At(45), moved: 0);
        schedule.Searched(At(225), moved: 0);
        Assert.Equal(TimeSpan.FromMinutes(6), schedule.Wait());

        schedule.Searched(At(600), moved: 1);

        Assert.Equal(TimeSpan.FromMinutes(3), schedule.Wait());
    }

    // "Procurar na rede" counts too: the Master doesn't search again right after it.
    [Fact]
    public void TheTeachersOwnSearch_PutsTheNextOneOff()
    {
        var schedule = new AddressSearchSchedule(Start);
        Assert.Null(FirstDue(schedule, 0, 40, Phone));
        schedule.Searched(At(42), moved: 0);

        Assert.Equal(At(42 + 180 + 3), FirstDue(schedule, 45, 1000, Phone));
    }
}
