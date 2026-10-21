using System.IO;
using System.Net.Sockets;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderSync: carrying out what the plan says for one file of one PC.
public sealed partial class ClassFolderSync
{
    private const string NotFound = "arquivo não encontrado"; // the Session's words

    // What a pull leaves free on the teacher's disk, at the least (about 1 GB):
    // the students' files must not fill the disk the teacher works on.
    private const long PullReserveBytes = ClassStore.LowSpaceBytes / 5;

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
                SyncAction.Push => Push(machine, lesson, key, action.Path, onPc.TryGetValue(action.Path, out var there) ? there : null),
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
    // replacing: what the listing the plan was made on had at that path (null: nothing),
    // the only thing the Session may replace: a student who saved the file since is
    // refused the push ("mudou neste PC") and keeps their file, for the next round.
    private string? Push(KnownMachine machine, ClassLesson lesson, string key, string path, FileVersion? replacing)
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
            machine, _masterCertificate, lesson.LessonId, path, full, version.LastWriteTicks, replacing, _sessionPort);
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
}
