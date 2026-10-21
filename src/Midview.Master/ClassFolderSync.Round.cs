using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderSync: one round with one PC.
public sealed partial class ClassFolderSync
{
    private const string OldMidview = "Midview antigo";
    private const string UpdatingLesson = "atualizando a aula";
    private const string CollectingText = "recolhendo arquivos de uma aula anterior";
    private const string NotFound = "arquivo não encontrado"; // the Session's words

    // What a pull leaves free on the teacher's disk, at the least (about 1 GB):
    // the students' files must not fill the disk the teacher works on.
    private const long PullReserveBytes = ClassStore.LowSpaceBytes / 5;

    // Each PC's last listing of the lesson: a file is only pulled once it held still between two.
    private readonly ConcurrentDictionary<string, (Guid LessonId, Dictionary<string, FileVersion> Files)> _listings =
        new(StringComparer.OrdinalIgnoreCase);

    // List the PC and plan its files against the store. Nothing to copy (most
    // rounds): done. Otherwise wait for a copy turn, list it again and plan again,
    // and carry out at most MaxActionsPerRound of that plan, stopping if the
    // lesson changes; the rest is for the next round. What the PC set aside for
    // lessons that are over is dealt with in the same turn. A file that fails
    // (held open, refused, its connection dropped) is only that file: the rest
    // goes on and it is tried again at the next round. A PC that can't be reached
    // any more ends the round. collectOnly: a lesson's last rounds, which only
    // bring files in, with no limit.
    private async Task RoundAsync(KnownMachine machine, ClassLesson lesson, bool collectOnly, CancellationToken token)
    {
        var listing = await ListAsync(machine, token).ConfigureAwait(false);
        if (!Usable(machine, listing))
        {
            return;
        }

        var key = Key(machine);
        var failures = new List<string>();
        var allowance = new Allowance(this, lesson, collectOnly, token);
        if (listing.CurrentLessonId != lesson.LessonId)
        {
            // Its Session isn't on this lesson (yet): the files in its folder wait.
            _listings.TryRemove(key, out _);
            var aside = SetAsideOf(listing, lesson);
            if (aside.Count > 0)
            {
                await CopyingAsync(() => CollectSetAside(machine, aside, failures, allowance), token).ConfigureAwait(false);
            }
            await UpdateLessonAsync(machine, lesson, token).ConfigureAwait(false);
            return;
        }

        var before = _listings.TryGetValue(key, out var last) && last.LessonId == lesson.LessonId ? last.Files : null;
        var first = PlanFor(machine, lesson, key, listing, before, collectOnly);
        if (first is null)
        {
            return;
        }
        _listings[key] = (lesson.LessonId, first.OnPc);
        if (first.Work.Count == 0 && SetAsideOf(listing, lesson).Count == 0)
        {
            Record(lesson.Turma, key, first.InSync);
            SaveIndex(lesson.Turma);
            Report(machine, PcSyncState.InSync, string.Empty);
            return;
        }

        // What is copied goes by a plan made with the turn in hand: the Session
        // replaces a file without a Recycle Bin, and a push planned while 30 other
        // PCs went first could land on what the student saved meanwhile. A file
        // counts as holding still only if it already did at the first look and
        // still does now (a turn that comes at once leaves too short a gap).
        Report(machine, PcSyncState.Copying, first.Work.Count > 0 ? CopyingText(first.Work.Count) : CollectingText);
        WaitingForCopySlot?.Invoke(machine);
        await _copying.WaitAsync(token).ConfigureAwait(false);
        var left = 0;
        try
        {
            if (allowance.Stopped)
            {
                return;
            }

            var fresh = await ListAsync(machine, token).ConfigureAwait(false);
            if (!Usable(machine, fresh))
            {
                return;
            }
            if (fresh.CurrentLessonId != lesson.LessonId)
            {
                _listings.TryRemove(key, out _);
                Report(machine, PcSyncState.Copying, UpdatingLesson);
                return;
            }

            var second = PlanFor(machine, lesson, key, fresh, Settled(before, first.OnPc), collectOnly);
            if (second is null)
            {
                return;
            }
            _listings[key] = (lesson.LessonId, second.OnPc);
            Record(lesson.Turma, key, second.InSync);
            var aside = SetAsideOf(fresh, lesson);
            left = await Task.Run(() =>
            {
                var done = 0;
                foreach (var action in second.Work)
                {
                    if (!allowance.Take())
                    {
                        break;
                    }
                    Execute(machine, lesson, key, action, second.OnPc, failures);
                    done++;
                }
                CollectSetAside(machine, aside, failures, allowance);
                return second.Work.Count - done;
            }, token).ConfigureAwait(false);
        }
        finally
        {
            _copying.Release();
            SaveIndex(lesson.Turma);
        }

        // A file that failed shows, with why ("a.docx (o arquivo está em uso por
        // outro programa)", a full disk), until a round gets it across.
        if (failures.Count > 0)
        {
            var more = failures.Count > 1 ? $" e mais {failures.Count - 1}" : string.Empty;
            Report(machine, PcSyncState.Failed, failures[0] + more);
        }
        else if (left > 0 || allowance.More)
        {
            Report(machine, PcSyncState.Copying, left > 0 ? CopyingText(left) : CollectingText);
        }
        else
        {
            Report(machine, PcSyncState.InSync, string.Empty);
        }
    }

    private static string CopyingText(int files) => files == 1 ? "copiando 1 arquivo" : $"copiando {files} arquivos";

    private Task<ClassFolderClient.ListResult> ListAsync(KnownMachine machine, CancellationToken token) =>
        Task.Run(() => ClassFolderClient.List(machine, _masterCertificate, _sessionPort), token);

    // A listing the round can go by; otherwise the PC's status says why not.
    private bool Usable(KnownMachine machine, ClassFolderClient.ListResult listing)
    {
        if (listing.OldSession)
        {
            Report(machine, PcSyncState.Failed, OldMidview);
            return false;
        }
        if (listing.Problem is { } problem)
        {
            Report(machine, PcSyncState.Failed, problem);
            return false;
        }
        return true;
    }

    // The files of a listing that the PC set aside for lessons that are over, by lesson.
    private static List<IGrouping<Guid, ClassFolderEntry>> SetAsideOf(ClassFolderClient.ListResult listing, ClassLesson lesson) =>
        listing.Entries
            .Where(entry => entry.LessonId != listing.CurrentLessonId && entry.LessonId != lesson.LessonId && entry.LessonId != Guid.Empty)
            .GroupBy(entry => entry.LessonId)
            .ToList();

    // What one listing of the PC needs: files already in sync, to record, and the
    // rest. Null, with the PC's status saying so, when the class's folder on this
    // PC can't be read (the round is skipped rather than planned on part of it).
    private PcPlan? PlanFor(
        KnownMachine machine, ClassLesson lesson, string key, ClassFolderClient.ListResult listing,
        IReadOnlyDictionary<string, FileVersion>? before, bool collectOnly)
    {
        var turma = lesson.Turma;
        IReadOnlyDictionary<string, FileVersion> stored;
        try
        {
            _store.RefreshTombstones(turma);
            stored = _store.Files(turma);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(machine, PcSyncState.Failed, $"a pasta da turma neste computador não pôde ser lida: {ErrorText.Describe(ex)}");
            return null;
        }

        var onPc = new Dictionary<string, FileVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in listing.Entries.Where(entry => entry.LessonId == lesson.LessonId))
        {
            onPc[entry.Path] = new FileVersion(entry.Size, entry.LastWriteTicks);
        }

        var plan = new PcPlan(onPc, new List<SyncAction.RecordInSync>(), new List<SyncAction>());
        var actions = ClassSyncPlanner.Plan(
            stored, path => _store.IsDeleted(turma, path), path => _store.LastSynced(turma, key, path), onPc, before);
        foreach (var action in actions)
        {
            if (action is SyncAction.RecordInSync inSync)
            {
                plan.InSync.Add(inSync);
            }
            else if (!collectOnly || action is SyncAction.Pull or SyncAction.PullAsNew)
            {
                plan.Work.Add(action);
            }
        }
        return plan;
    }

    // The files of the first look that had held still since the round before:
    // only they may count as holding still at the second look.
    private static Dictionary<string, FileVersion>? Settled(
        IReadOnlyDictionary<string, FileVersion>? before, Dictionary<string, FileVersion> first)
    {
        if (before is null)
        {
            return null;
        }

        var settled = new Dictionary<string, FileVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, version) in first)
        {
            if (before.TryGetValue(path, out var earlier) && earlier == version)
            {
                settled[path] = version;
            }
        }
        return settled;
    }

    private void Record(string turma, string key, IEnumerable<SyncAction.RecordInSync> inSync)
    {
        foreach (var file in inSync)
        {
            _store.RecordSynced(turma, key, file.Path, file.Version);
        }
    }

    // At most MaxCopyingPcs PCs copying at once. The copying blocks a thread of
    // its own (ClassFolderClient is synchronous); waiting for a turn doesn't.
    private async Task CopyingAsync(Action copy, CancellationToken token)
    {
        await _copying.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await Task.Run(copy, token).ConfigureAwait(false);
        }
        finally
        {
            _copying.Release();
        }
    }

    // A PC whose Session is on another lesson: its Agent gets this one, unless it
    // took it already (the Session follows within a few seconds).
    private async Task UpdateLessonAsync(KnownMachine machine, ClassLesson lesson, CancellationToken token)
    {
        if (!_delivered.TryGetValue(Key(machine), out var savedAt) || savedAt != lesson.SavedAt)
        {
            var (_, sent, detail) = await Task.Run(() => SendLesson(machine, lesson), token).ConfigureAwait(false);
            if (!sent)
            {
                Report(machine, PcSyncState.Failed, detail == CommandClient.OlderSession ? OldMidview : detail);
                return;
            }
        }
        Report(machine, PcSyncState.Copying, UpdatingLesson);
    }

    private void SaveIndex(string turma)
    {
        try
        {
            _store.SaveIndex(turma);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The index of {turma} could not be saved yet: {ex.Message}");
        }
    }

    // --- The plan ---

    // A PC that can't be reached any more (SocketException, a connect that timed
    // out) ends the round: every other file would wait for the same timeout.
    private void Execute(
        KnownMachine machine, ClassLesson lesson, string key, SyncAction action,
        IReadOnlyDictionary<string, FileVersion> onPc, List<string> failures)
    {
        string? why;
        try
        {
            why = action switch
            {
                SyncAction.Push => Push(machine, lesson, key, action.Path),
                SyncAction.Pull pull => Pull(machine, lesson, key, action.Path, onPc[action.Path], pull.Replace),
                SyncAction.PullAsNew => Pull(machine, lesson, key, action.Path, onPc[action.Path], replace: false),
                SyncAction.DeleteOnPc => DeleteOnPc(machine, lesson, action.Path),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is not (SocketException or OperationCanceledException))
        {
            why = FailureText.Describe(ex);
        }

        if (why is not null)
        {
            failures.Add($"{action.Path} ({why})");
        }
    }

    // The store's file as it is on the disk now, which is the version the PC ends up with.
    private string? Push(KnownMachine machine, ClassLesson lesson, string key, string path)
    {
        string full;
        FileVersion version;
        try
        {
            full = _store.PathOf(lesson.Turma, path);
            var file = new FileInfo(full);
            if (!file.Exists)
            {
                return null; // deleted since the listing: the next round knows
            }
            version = new FileVersion(file.Length, file.LastWriteTimeUtc.Ticks);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return ErrorText.Describe(ex);
        }

        var (pushed, why) = ClassFolderClient.Push(
            machine, _masterCertificate, lesson.LessonId, path, full, version.LastWriteTicks, _sessionPort);
        if (!pushed)
        {
            return why;
        }
        _store.RecordSynced(lesson.Turma, key, path, version);
        return null;
    }

    // An edit (replace) or a new file. A new file whose name was taken in the
    // store meanwhile, or a different file with a name the class has
    // (PullAsNew), is stored under a free name, and the PC's copy is renamed to
    // match. Both paths are recorded as the PC's version first, so if the rename
    // fails the class's own file is pushed back over that path at the next round
    // (the PC's work being in the store already) rather than pulled once more.
    private string? Pull(KnownMachine machine, ClassLesson lesson, string key, string path, FileVersion version, bool replace)
    {
        var (stored, why) = PullInto(machine, lesson.Turma, lesson.LessonId, path, version, replace);
        if (stored is null)
        {
            return why;
        }

        _store.RecordSynced(lesson.Turma, key, path, version);
        if (string.Equals(stored, path, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        _store.RecordSynced(lesson.Turma, key, stored, version);
        var (renamed, renameWhy) = ClassFolderClient.Rename(machine, _masterCertificate, lesson.LessonId, path, stored, _sessionPort);
        return renamed ? null : $"não foi possível renomear para {stored}: {renameWhy}";
    }

    private string? DeleteOnPc(KnownMachine machine, ClassLesson lesson, string path)
    {
        var (deleted, why) = ClassFolderClient.Delete(machine, _masterCertificate, lesson.LessonId, path, _sessionPort);
        return deleted || why == NotFound ? null : why;
    }

    // The PC's file into the class's store, through a temp file in it: the path
    // it was stored at, or null and why not. Nothing is left behind on failure.
    // Only while the teacher's disk keeps PullReserveBytes to spare after it.
    private (string? Stored, string Why) PullInto(
        KnownMachine machine, string turma, Guid lessonId, string path, FileVersion version, bool replace)
    {
        if (_store.FreeBytes() < version.Size + PullReserveBytes)
        {
            return (null, FileCollector.NoSpaceText);
        }

        FileStream incoming;
        try
        {
            incoming = _store.BeginIncoming(turma);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, ErrorText.Describe(ex));
        }

        string? leftover = incoming.Name;
        try
        {
            (bool Succeeded, string Detail) pulled;
            using (incoming)
            {
                pulled = ClassFolderClient.Pull(machine, _masterCertificate, lessonId, path, incoming, _sessionPort);
            }
            if (!pulled.Succeeded)
            {
                return (null, pulled.Detail);
            }

            try
            {
                var stored = _store.CommitIncoming(turma, leftover, path, version, replace);
                leftover = null;
                return (stored, string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return (null, ErrorText.Describe(ex));
            }
        }
        finally
        {
            if (leftover is not null)
            {
                TryDelete(leftover);
            }
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"A temp file could not be deleted: {path}: {ex.Message}");
        }
    }

    // What one listing of a PC needs (PlanFor).
    private sealed record PcPlan(
        Dictionary<string, FileVersion> OnPc, List<SyncAction.RecordInSync> InSync, List<SyncAction> Work);

    // What a round may still do: MaxActionsPerRound copies (a lesson's last
    // rounds have no limit), and nothing once Dispose came or the lesson changed,
    // or is changing, under it.
    private sealed class Allowance
    {
        private readonly ClassFolderSync _sync;
        private readonly ClassLesson _lesson;
        private readonly bool _collectOnly;
        private readonly CancellationToken _token;
        private int _left;

        public Allowance(ClassFolderSync sync, ClassLesson lesson, bool collectOnly, CancellationToken token)
        {
            _sync = sync;
            _lesson = lesson;
            _collectOnly = collectOnly;
            _token = token;
            _left = collectOnly ? int.MaxValue : MaxActionsPerRound;
        }

        // Something was left for the next round.
        public bool More { get; private set; }

        public bool Stopped =>
            _token.IsCancellationRequested
            || _sync.Lesson.LessonId != _lesson.LessonId
            || (!_collectOnly && _sync._changingLesson);

        // Whether one more copy may go now. Throws when Dispose came.
        public bool Take()
        {
            _token.ThrowIfCancellationRequested();
            if (Stopped || _left == 0)
            {
                More = true;
                return false;
            }
            _left--;
            return true;
        }
    }
}
