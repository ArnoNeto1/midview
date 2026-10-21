using Midview.Master;
using Xunit;

public class ClassSyncPlannerTests
{
    // Three versions of a file; the sync tells them apart by size and time only.
    private static readonly FileVersion V1 = new(100, 1_000);
    private static readonly FileVersion V2 = new(200, 2_000);
    private static readonly FileVersion V3 = new(300, 3_000);

    private static Dictionary<string, FileVersion> Files(params (string Path, FileVersion Version)[] files) =>
        files.ToDictionary(file => file.Path, file => file.Version, StringComparer.OrdinalIgnoreCase);

    // What the Master asks of the store: lastSynced (this PC's record) and isTombstone by path,
    // ignoring case as ClassStore does.
    private static IReadOnlyList<SyncAction> Plan(
        Dictionary<string, FileVersion> store,
        Dictionary<string, FileVersion> pcNow,
        Dictionary<string, FileVersion>? pcBefore,
        Dictionary<string, FileVersion>? synced = null,
        params string[] deleted)
    {
        var records = synced ?? Files();
        var tombstones = new HashSet<string>(deleted, StringComparer.OrdinalIgnoreCase);
        return ClassSyncPlanner.Plan(
            store,
            tombstones.Contains,
            path => records.TryGetValue(path, out var version) ? version : null,
            pcNow,
            pcBefore);
    }

    [Fact]
    public void InSync_IsRecorded()
    {
        var files = Files(("a.txt", V1));

        var plan = Plan(files, files, pcBefore: files);

        Assert.Equal(new SyncAction[] { new SyncAction.RecordInSync("a.txt", V1) }, plan);
    }

    [Fact]
    public void StoreMovedOn_IsPushed()
    {
        var plan = Plan(
            Files(("a.txt", V2)), Files(("a.txt", V1)), pcBefore: Files(("a.txt", V1)), synced: Files(("a.txt", V1)));

        Assert.Equal(new SyncAction[] { new SyncAction.Push("a.txt") }, plan);
    }

    [Fact]
    public void PcEdited_IsPulledReplacing()
    {
        var plan = Plan(
            Files(("a.txt", V1)), Files(("a.txt", V2)), pcBefore: Files(("a.txt", V2)), synced: Files(("a.txt", V1)));

        Assert.Equal(new SyncAction[] { new SyncAction.Pull("a.txt", Replace: true) }, plan);
    }

    [Fact]
    public void SameNameDifferentFile_IsPulledAsNew()
    {
        // The PC never had a.txt from the store, and the store has another one.
        var plan = Plan(Files(("a.txt", V1)), Files(("a.txt", V2)), pcBefore: Files(("a.txt", V2)));

        Assert.Equal(new SyncAction[] { new SyncAction.PullAsNew("a.txt") }, plan);
    }

    [Fact]
    public void NewOnPc_IsPulled()
    {
        var plan = Plan(Files(), Files(("a.txt", V1)), pcBefore: Files(("a.txt", V1)));

        Assert.Equal(new SyncAction[] { new SyncAction.Pull("a.txt", Replace: false) }, plan);
    }

    [Fact]
    public void TeacherDeleted_IsDeletedOnPc()
    {
        var plan = Plan(
            Files(), Files(("a.txt", V1)), pcBefore: Files(("a.txt", V1)), synced: Files(("a.txt", V1)), "a.txt");

        Assert.Equal(new SyncAction[] { new SyncAction.DeleteOnPc("a.txt") }, plan);
    }

    [Fact]
    public void TeacherDeletedButPcEdited_IsPulled()
    {
        // The student's changes are worth more than the teacher's deletion: the file comes back.
        var plan = Plan(
            Files(), Files(("a.txt", V2)), pcBefore: Files(("a.txt", V2)), synced: Files(("a.txt", V1)), "a.txt");

        Assert.Equal(new SyncAction[] { new SyncAction.Pull("a.txt", Replace: false) }, plan);
    }

    [Fact]
    public void StudentDeleted_IsPushedBack()
    {
        var plan = Plan(Files(("a.txt", V1)), Files(), pcBefore: Files(), synced: Files(("a.txt", V1)));

        Assert.Equal(new SyncAction[] { new SyncAction.Push("a.txt") }, plan);
    }

    [Fact]
    public void NewInStore_IsPushed()
    {
        var plan = Plan(Files(("a.txt", V1)), Files(), pcBefore: Files());

        Assert.Equal(new SyncAction[] { new SyncAction.Push("a.txt") }, plan);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnstableFile_IsNotPulled(bool absentFromTheLastListing)
    {
        // An edit, a same-name file and a new file, each still changing (or just seen).
        var store = Files(("edit.txt", V1), ("same.txt", V1));
        var pcNow = Files(("edit.txt", V2), ("same.txt", V2), ("new.txt", V2));
        var pcBefore = absentFromTheLastListing
            ? Files()
            : Files(("edit.txt", V3), ("same.txt", V3), ("new.txt", V3));

        var plan = Plan(store, pcNow, pcBefore, synced: Files(("edit.txt", V1)));

        Assert.Empty(plan);
    }

    [Fact]
    public void FirstRound_PullsNothing()
    {
        // The PC was never listed before, so no file of it has been seen to hold still:
        // only what the store holds (a push) and what is already equal (a record) is planned.
        var store = Files(("kept.txt", V1), ("edit.txt", V1), ("missing.txt", V1));
        var pcNow = Files(("kept.txt", V1), ("edit.txt", V2), ("new.txt", V1), ("other.txt", V3));

        var plan = Plan(store, pcNow, pcBefore: null, synced: Files(("edit.txt", V1)));

        Assert.Equal(
            new SyncAction[] { new SyncAction.RecordInSync("kept.txt", V1), new SyncAction.Push("missing.txt") },
            plan);
    }

    [Fact]
    public void FirstRound_StillDeletesWhatTheTeacherDeleted()
    {
        // Deleting a file the PC still has at the version it got needs no waiting: nothing is lost.
        var plan = Plan(
            Files(), Files(("gone.txt", V1)), pcBefore: null, synced: Files(("gone.txt", V1)), "gone.txt");

        Assert.Equal(new SyncAction[] { new SyncAction.DeleteOnPc("gone.txt") }, plan);
    }

    [Fact]
    public void PathsCompareIgnoringCase()
    {
        // Plain dictionaries, which compare ordinally: the planner must not lean on that.
        var store = new Dictionary<string, FileVersion>
        {
            ["Fotos/Mapa.PNG"] = V1,
            ["Notas.TXT"] = V1,
            ["Lido.txt"] = V1,
        };
        var pcNow = new Dictionary<string, FileVersion>
        {
            ["fotos/mapa.png"] = V2,
            ["LIDO.TXT"] = V1,
        };
        var pcBefore = new Dictionary<string, FileVersion> { ["FOTOS/MAPA.PNG"] = V2, ["lido.TXT"] = V1 };

        var plan = ClassSyncPlanner.Plan(
            store,
            _ => false,
            path => string.Equals(path, "Fotos/MAPA.png", StringComparison.OrdinalIgnoreCase) ? V1 : null,
            pcNow,
            pcBefore);

        // A file the PC has is named as the PC spells it; one only the store has, as the store does.
        Assert.Equal(
            new SyncAction[]
            {
                new SyncAction.Pull("fotos/mapa.png", Replace: true),
                new SyncAction.RecordInSync("LIDO.TXT", V1),
                new SyncAction.Push("Notas.TXT"),
            },
            plan);
    }

    [Fact]
    public void ActionsAreSortedByPath()
    {
        var store = new Dictionary<string, FileVersion>(StringComparer.OrdinalIgnoreCase)
        {
            ["d.txt"] = V1,
            ["b.txt"] = V1,
            ["A.txt"] = V1,
        };
        var pcNow = Files(("c.txt", V1), ("A.txt", V1));

        var plan = Plan(store, pcNow, pcBefore: pcNow);

        Assert.Equal(new[] { "A.txt", "b.txt", "c.txt", "d.txt" }, plan.Select(action => action.Path));
    }

    [Fact]
    public void NothingAnywhere_PlansNothing()
    {
        Assert.Empty(Plan(Files(), Files(), pcBefore: null));
    }
}
