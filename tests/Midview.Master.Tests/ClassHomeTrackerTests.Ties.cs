using Xunit;

// ClassHomeTracker between homes reported at the very same generation: two
// PCs go by the name that sorts last, as a start decides between them
// (ClassFolderSync.Contest.cs), so both teacher PCs see the same home; one PC
// recorded under two certificates goes by the record most Agents report.
public sealed partial class ClassHomeTrackerTests
{
    // Two PCs of the same generation: the name that sorts last, ignoring
    // case, however many Agents report the other.
    [Fact]
    public async Task PcsOfTheSameGeneration_TheNameThatSortsLastIsTheHome()
    {
        using var rig = new ClassSyncRig(homes: 3);
        Claim(rig, rig.Pcs[0], rig.OtherMaster, OtherPc, Older);
        Claim(rig, rig.Pcs[1], rig.OtherMaster, OtherPc, Older);
        Claim(rig, rig.Pcs[2], rig.Master, "prof-3", Older);
        using var tracker = NewTracker(rig);

        await tracker.RefreshAsync();

        Assert.Equal("prof-3", tracker.Home?.Name);
    }

    // One PC recorded under two certificates at one generation (reinstalled,
    // say): the record most Agents report, and on a tie the one the tracker
    // already has, whichever that is.
    [Fact]
    public async Task OnePcRecordedTwice_ATie_KeepsTheCurrentRecord()
    {
        using var rig = new ClassSyncRig(homes: 3);
        Claim(rig, rig.Pcs[0], rig.OtherMaster, OtherPc, Older);
        Claim(rig, rig.Pcs[1], rig.OtherMaster, OtherPc, Older);
        Claim(rig, rig.Pcs[2], rig.Master, OtherPc, Older);

        using (var tracker = NewTracker(rig))
        {
            await tracker.RefreshAsync();
            Assert.Equal(rig.OtherMaster.Thumbprint, tracker.Home?.Thumbprint, ignoreCase: true);

            rig.Offline[rig.Pcs[0].Name] = true; // one each now
            for (var refresh = 0; refresh < 5; refresh++)
            {
                await tracker.RefreshAsync();
                Assert.Equal(rig.OtherMaster.Thumbprint, tracker.Home?.Thumbprint, ignoreCase: true);
            }
        }

        rig.Offline[rig.Pcs[1].Name] = true;
        using (var tracker = NewTracker(rig))
        {
            await tracker.RefreshAsync();
            Assert.Equal(rig.Master.Thumbprint, tracker.Home?.Thumbprint, ignoreCase: true);

            rig.Offline.TryRemove(rig.Pcs[1].Name, out _); // one each now
            for (var refresh = 0; refresh < 5; refresh++)
            {
                await tracker.RefreshAsync();
                Assert.Equal(rig.Master.Thumbprint, tracker.Home?.Thumbprint, ignoreCase: true);
            }
        }
    }
}
