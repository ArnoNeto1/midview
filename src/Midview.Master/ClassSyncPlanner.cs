namespace Midview.Master;

// What the sync does about one file of a class on one PC (ClassSyncPlanner decides).
// Path is '/'-separated, spelled as the PC has it when the PC has the file, and as
// the store has it otherwise.
public abstract record SyncAction(string Path)
{
    // Copy the PC's file into the store. Replace: it is an edit of the store's file
    // at that path (the old copy is set aside); otherwise the store has no file there.
    public sealed record Pull(string Path, bool Replace) : SyncAction(Path);

    // Copy the PC's file into the store under a free name ("images (2).jpg"), then
    // rename it on the PC to that name: a different file that has the same name.
    public sealed record PullAsNew(string Path) : SyncAction(Path);

    // Copy the store's file to the PC, over the one there if any.
    public sealed record Push(string Path) : SyncAction(Path);

    // Delete the file on the PC: the teacher deleted it, and the PC still has the
    // version it got.
    public sealed record DeleteOnPc(string Path) : SyncAction(Path);

    // Nothing to copy: the PC's file is the store's. Version is what the PC has.
    public sealed record RecordInSync(string Path, FileVersion Version) : SyncAction(Path);
}

// Decides, for one class on one PC, what each file needs ("Pasta da turma",
// docs/superpowers/specs/2026-10-20-class-folder-design.md, "Synchronisation" and
// "Changes and conflicts"). Pure: no I/O, and the same input gives the same actions.
//
// A PC's file is judged by the version that PC last had of that path (lastSynced), not
// by clocks, so a PC whose clock runs behind loses no edit:
//   - the same as the store's: in sync;
//   - not the store's, and still what the PC last had: the store moved on, push;
//   - not the store's, and not what the PC last had: an edit, which replaces the store's
//     copy (two PCs editing one file between rounds: the later to be pulled wins);
//   - not the store's, and a path the PC never had: a different file with the same name,
//     kept under a free name;
//   - not in the store: new on the PC, pulled, unless the teacher deleted it and the PC
//     still has the version it got, when it is deleted there.
// A file only the store has is pushed (a PC that just came online gets the whole class,
// and a student who deleted a file gets it back).
//
// Nothing is pulled until the file has held still between two listings of the PC
// (pcBefore, null for the PC's first round), so a download in progress or a document
// being saved isn't copied half-written. Recording, pushing and deleting need no such
// wait: they only happen where the PC has no file, or has the store's, or still has the
// version it last got, so nothing the student is changing is overwritten or removed.
public static class ClassSyncPlanner
{
    // Paths compare ignoring case whatever the comparers of the dictionaries given: they
    // are copied, so a plain Dictionary works as well as ClassStore's. The actions come
    // sorted by path, so tests and logs are stable.
    public static IReadOnlyList<SyncAction> Plan(
        IReadOnlyDictionary<string, FileVersion> store,
        Func<string, bool> isTombstone,
        Func<string, FileVersion?> lastSynced,
        IReadOnlyDictionary<string, FileVersion> pcNow,
        IReadOnlyDictionary<string, FileVersion>? pcBefore)
    {
        var inStore = IgnoringCase(store);
        var onPc = IgnoringCase(pcNow);
        var earlier = pcBefore is null ? null : IgnoringCase(pcBefore);

        var actions = new List<SyncAction>();
        foreach (var (path, version) in onPc)
        {
            var stable = earlier is not null && earlier.TryGetValue(path, out var before) && before == version;
            if (ForFileOnPc(path, version, stable, inStore, isTombstone, lastSynced) is { } action)
            {
                actions.Add(action);
            }
        }

        foreach (var path in inStore.Keys)
        {
            if (!onPc.ContainsKey(path))
            {
                actions.Add(new SyncAction.Push(path));
            }
        }

        // A path is in the list once, so the order is complete.
        actions.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        return actions;
    }

    // What a file the PC has needs, or null for nothing this round.
    private static SyncAction? ForFileOnPc(
        string path,
        FileVersion pc,
        bool stable,
        Dictionary<string, FileVersion> inStore,
        Func<string, bool> isTombstone,
        Func<string, FileVersion?> lastSynced)
    {
        if (inStore.TryGetValue(path, out var stored))
        {
            if (stored == pc)
            {
                return new SyncAction.RecordInSync(path, pc);
            }

            var synced = lastSynced(path);
            if (synced == pc)
            {
                return new SyncAction.Push(path);
            }

            if (!stable)
            {
                return null;
            }
            return synced is null ? new SyncAction.PullAsNew(path) : new SyncAction.Pull(path, Replace: true);
        }

        if (isTombstone(path) && lastSynced(path) == pc)
        {
            return new SyncAction.DeleteOnPc(path);
        }
        return stable ? new SyncAction.Pull(path, Replace: false) : null;
    }

    private static Dictionary<string, FileVersion> IgnoringCase(IReadOnlyDictionary<string, FileVersion> files)
    {
        var copy = new Dictionary<string, FileVersion>(files.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (path, version) in files)
        {
            copy[path] = version;
        }
        return copy;
    }
}
