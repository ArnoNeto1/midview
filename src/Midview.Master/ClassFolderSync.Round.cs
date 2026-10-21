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

    // Each PC's last listing of the lesson: a file is only pulled once it held still between two.
    private readonly ConcurrentDictionary<string, (Guid LessonId, Dictionary<string, FileVersion> Files)> _listings =
        new(StringComparer.OrdinalIgnoreCase);

    // Files set aside on a PC that are in their class now, while clearing them on the PC
    // hasn't worked yet: not pulled again (that would make a "(2)" of each).
    private readonly ConcurrentDictionary<(string Key, Guid LessonId, string Path, FileVersion Version), byte> _pulledAside = new();

    // List the PC; deal with what it set aside for lessons that are over; then,
    // if its Session is on this lesson, plan the class's files against the store
    // and carry the plan out. A file that fails (held open, refused, its
    // connection dropped) is only that file: the rest goes on and it is tried
    // again at the next round. A PC that can't be reached any more ends the round.
    // collectOnly: a lesson's last rounds, which only bring files in.
    private async Task RoundAsync(KnownMachine machine, ClassLesson lesson, bool collectOnly, CancellationToken token)
    {
        var listing = await Task.Run(() => ClassFolderClient.List(machine, _masterCertificate, _sessionPort), token)
            .ConfigureAwait(false);
        if (listing.OldSession)
        {
            Report(machine, PcSyncState.Failed, OldMidview);
            return;
        }
        if (listing.Problem is { } problem)
        {
            Report(machine, PcSyncState.Failed, problem);
            return;
        }

        var key = Key(machine);
        var failures = new List<string>();
        var setAside = listing.Entries
            .Where(entry => entry.LessonId != listing.CurrentLessonId && entry.LessonId != lesson.LessonId && entry.LessonId != Guid.Empty)
            .GroupBy(entry => entry.LessonId)
            .ToList();

        if (listing.CurrentLessonId != lesson.LessonId)
        {
            // Its Session isn't on this lesson (yet): the files in its folder wait.
            _listings.TryRemove(key, out _);
            if (setAside.Count > 0)
            {
                await CopyingAsync(() => CollectSetAside(machine, key, setAside, failures), token).ConfigureAwait(false);
            }
            await UpdateLessonAsync(machine, lesson, token).ConfigureAwait(false);
            return;
        }

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
            return;
        }

        var onPc = new Dictionary<string, FileVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in listing.Entries.Where(entry => entry.LessonId == lesson.LessonId))
        {
            onPc[entry.Path] = new FileVersion(entry.Size, entry.LastWriteTicks);
        }
        var before = _listings.TryGetValue(key, out var last) && last.LessonId == lesson.LessonId ? last.Files : null;
        _listings[key] = (lesson.LessonId, onPc);

        var plan = ClassSyncPlanner.Plan(
            stored, path => _store.IsDeleted(turma, path), path => _store.LastSynced(turma, key, path), onPc, before);
        var work = new List<SyncAction>();
        foreach (var action in plan)
        {
            if (action is SyncAction.RecordInSync inSync)
            {
                _store.RecordSynced(turma, key, inSync.Path, inSync.Version);
            }
            else if (!collectOnly || action is SyncAction.Pull or SyncAction.PullAsNew)
            {
                work.Add(action);
            }
        }

        try
        {
            if (work.Count > 0 || setAside.Count > 0)
            {
                Report(machine, PcSyncState.Copying, work.Count > 0 ? CopyingText(work.Count) : CollectingText);
                await CopyingAsync(() =>
                {
                    CollectSetAside(machine, key, setAside, failures);
                    foreach (var action in work)
                    {
                        token.ThrowIfCancellationRequested();
                        Execute(machine, lesson, key, action, onPc, failures);
                    }
                }, token).ConfigureAwait(false);
            }
        }
        finally
        {
            SaveIndex(turma);
        }

        // A file that failed shows, with why ("a.docx (o arquivo está em uso por
        // outro programa)", a full disk), until a round gets it across.
        if (failures.Count == 0)
        {
            Report(machine, PcSyncState.InSync, string.Empty);
        }
        else
        {
            var more = failures.Count > 1 ? $" e mais {failures.Count - 1}" : string.Empty;
            Report(machine, PcSyncState.Failed, failures[0] + more);
        }
    }

    private static string CopyingText(int files) => files == 1 ? "copiando 1 arquivo" : $"copiando {files} arquivos";

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
    private (string? Stored, string Why) PullInto(
        KnownMachine machine, string turma, Guid lessonId, string path, FileVersion version, bool replace)
    {
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

    // --- Files set aside for lessons that are over ---

    // A lesson "Encerrar a aula" collected: what the PC set aside afterwards is
    // its own (it was offline at the end), so it is only cleared, to the
    // student's Recycle Bin. A lesson whose class is unknown or gone: the same.
    // Any other (it ended with the day, or the class was changed): each file the
    // class hasn't got (the same path at the same version) comes in as a new file,
    // and once all have, the PC clears them.
    private void CollectSetAside(
        KnownMachine machine, string key, IReadOnlyList<IGrouping<Guid, ClassFolderEntry>> lessons, List<string> failures)
    {
        foreach (var files in lessons)
        {
            var lessonId = files.Key;
            if (!_store.IsCollected(lessonId) && _store.TurmaOfLesson(lessonId) is { } turma && _store.Exists(turma)
                && !PullSetAside(machine, key, lessonId, turma, files, failures))
            {
                continue; // what is missing is tried again at the next round; nothing is cleared before
            }

            var (cleared, why) = ClassFolderClient.ClearSetAside(machine, _masterCertificate, lessonId, _sessionPort);
            if (cleared)
            {
                foreach (var pulled in _pulledAside.Keys.Where(pulled => pulled.Key == key && pulled.LessonId == lessonId))
                {
                    _pulledAside.TryRemove(pulled, out _);
                }
            }
            else
            {
                failures.Add($"arquivos de uma aula anterior ({why})");
            }
        }
    }

    // True when every file is in the class now.
    private bool PullSetAside(
        KnownMachine machine, string key, Guid lessonId, string turma, IEnumerable<ClassFolderEntry> files, List<string> failures)
    {
        IReadOnlyDictionary<string, FileVersion> stored;
        try
        {
            stored = _store.Files(turma);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add($"turma {turma} ({ErrorText.Describe(ex)})");
            return false;
        }

        var complete = true;
        foreach (var file in files)
        {
            var version = new FileVersion(file.Size, file.LastWriteTicks);
            var pulled = (key, lessonId, file.Path, version);
            if ((stored.TryGetValue(file.Path, out var have) && have == version) || _pulledAside.ContainsKey(pulled))
            {
                continue; // the class has it: synced during the lesson, or brought in by an earlier round
            }

            string? why;
            try
            {
                var (at, reason) = PullInto(machine, turma, lessonId, file.Path, version, replace: false);
                why = at is null ? reason : null;
            }
            catch (Exception ex) when (ex is not (SocketException or OperationCanceledException))
            {
                why = FailureText.Describe(ex);
            }

            if (why is null)
            {
                _pulledAside.TryAdd(pulled, 0);
            }
            else
            {
                complete = false;
                failures.Add($"{file.Path} ({why})");
            }
        }
        return complete;
    }
}
