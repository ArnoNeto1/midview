using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassHomeTracker.Refreshed: every refresh says what it found, changed or
// not, and how many online Agents it asked (ClassFolderRole goes by two
// readings in a row, and hands no lesson out before one that asked an Agent).
public sealed partial class ClassHomeTrackerTests
{
    [Fact]
    public async Task EveryRefresh_SaysWhatItFound_AndHowManyAgentsItAsked()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var tracker = NewTracker(rig);
        var readings = new List<ClassHomeReading>();
        tracker.Refreshed += reading =>
        {
            lock (readings)
            {
                readings.Add(reading);
            }
        };
        foreach (var pc in rig.Pcs)
        {
            rig.Offline[pc.Name] = true;
        }

        await tracker.RefreshAsync(); // nobody on
        rig.Offline.Clear();
        await tracker.RefreshAsync(); // no home yet: unchanged, still told
        foreach (var pc in rig.Pcs)
        {
            Claim(rig, pc, rig.OtherMaster, OtherPc);
        }
        rig.Offline[rig.Pcs[2].Name] = true;
        await tracker.RefreshAsync();

        lock (readings)
        {
            Assert.Equal(3, readings.Count);
            Assert.Equal(new ClassHomeReading(null, ClassFolderMode.Local, 0), readings[0] with { StartedAt = 0 });
            Assert.Equal(new ClassHomeReading(null, ClassFolderMode.Local, 3), readings[1] with { StartedAt = 0 });
            Assert.True(readings[0].StartedAt > 0 && readings[0].StartedAt <= readings[1].StartedAt && readings[1].StartedAt <= readings[2].StartedAt);
            Assert.Equal(ClassFolderMode.Remote, readings[2].Mode);
            Assert.Equal(OtherPc, readings[2].Home?.Name);
            Assert.Equal(2, readings[2].Asked);
            Assert.Equal(tracker.Home, readings[2].Home);
        }
    }

    // Whether this PC's class-home.json was read for it (Fix round 1, minor 1):
    // missing is read (none of it); held open by another program is not.
    [Fact]
    public async Task AReading_SaysWhetherThisPcsFileWasRead()
    {
        using var rig = new ClassSyncRig(homes: 1, pcs: 1);
        using var tracker = NewTracker(rig);
        var readings = new List<ClassHomeReading>();
        tracker.Refreshed += reading => readings.Add(reading);

        await tracker.RefreshAsync();
        ClassHomeFileMaster.Write(Older, rig.HomePath);
        await tracker.RefreshAsync();
        using (new FileStream(rig.HomePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await tracker.RefreshAsync();
        }

        Assert.Equal(new[] { true, true, false }, readings.Select(reading => reading.FileRead));
    }

    // Raised after Changed, so a handler of either sees the home already taken.
    [Fact]
    public async Task Refreshed_ComesAfterChanged()
    {
        using var rig = new ClassSyncRig(homes: 1, pcs: 1);
        using var tracker = NewTracker(rig);
        var order = new List<string>();
        tracker.Changed += () => order.Add("changed");
        tracker.Refreshed += reading => order.Add($"refreshed {reading.Mode} {tracker.Mode}");
        Claim(rig, rig.Pcs[0], rig.OtherMaster, OtherPc);

        await tracker.RefreshAsync();

        Assert.Equal(new[] { "changed", "refreshed Remote Remote" }, order);
    }
}
