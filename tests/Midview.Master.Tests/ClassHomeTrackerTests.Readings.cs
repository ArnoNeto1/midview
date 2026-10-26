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
            Assert.Equal(new ClassHomeReading(null, ClassFolderMode.Local, 0), readings[0]);
            Assert.Equal(new ClassHomeReading(null, ClassFolderMode.Local, 3), readings[1]);
            Assert.Equal(ClassFolderMode.Remote, readings[2].Mode);
            Assert.Equal(OtherPc, readings[2].Home?.Name);
            Assert.Equal(2, readings[2].Asked);
            Assert.Equal(tracker.Home, readings[2].Home);
        }
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
