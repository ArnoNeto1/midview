using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderSync: what a PC set aside for lessons that are over.
public sealed partial class ClassFolderSync
{
    // Set-aside files a round is bringing into their class right now, by lesson, path
    // (upper-cased) and version: the same file set aside on another PC waits for it
    // rather than becoming a "(2)".
    private readonly ConcurrentDictionary<(Guid LessonId, string Path, FileVersion Version), byte> _pullingAside = new();

    // A lesson "Encerrar a aula" collected: what the PC set aside afterwards is
    // its own (it was offline at the end), so it is only cleared, to the
    // student's Recycle Bin. A lesson whose class is unknown or gone: the same.
    // Any other (it ended with the day, or the class was changed): each file the
    // class hasn't got comes in as a new file, and once all have, the PC clears
    // them. "Hasn't got": not the same path at the same version in the class, nor
    // a version remembered for the lesson (what the class and its PCs had when it
    // ended, ClassStore.RememberVersionsOf, and what any PC's set-aside files
    // brought in since), so the same file on 30 PCs comes in once, and one the
    // teacher deleted doesn't come back.
    private void CollectSetAside(
        KnownMachine machine, IReadOnlyList<IGrouping<Guid, ClassFolderEntry>> lessons, List<string> failures, Allowance allowance)
    {
        foreach (var files in lessons)
        {
            if (allowance.Stopped)
            {
                return;
            }

            var lessonId = files.Key;
            if (!_store.IsCollected(lessonId) && _store.TurmaOfLesson(lessonId) is { } turma && _store.Exists(turma)
                && !PullSetAside(machine, lessonId, turma, files, failures, allowance))
            {
                continue; // what is missing comes at a later round; nothing is cleared before
            }

            var (cleared, why) = ClassFolderClient.ClearSetAside(machine, _masterCertificate, lessonId, _sessionPort);
            if (!cleared)
            {
                failures.Add($"arquivos de uma aula anterior ({why})");
            }
        }
    }

    // True when every file is in the class now.
    private bool PullSetAside(
        KnownMachine machine, Guid lessonId, string turma, IEnumerable<ClassFolderEntry> files, List<string> failures,
        Allowance allowance)
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
            if ((stored.TryGetValue(file.Path, out var have) && have == version) || _store.IsKnownVersion(lessonId, file.Path, version))
            {
                continue;
            }

            var claim = (lessonId, file.Path.ToUpperInvariant(), version);
            if (!_pullingAside.TryAdd(claim, 0))
            {
                complete = false; // another PC's round is bringing the same file in: cleared at a later round
                continue;
            }

            try
            {
                if (_store.IsKnownVersion(lessonId, file.Path, version))
                {
                    continue; // brought in between the look and the claim
                }
                if (!allowance.Take())
                {
                    complete = false;
                    break;
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
                    _store.RememberVersion(lessonId, file.Path, version);
                }
                else
                {
                    complete = false;
                    failures.Add($"{file.Path} ({why})");
                }
            }
            finally
            {
                _pullingAside.TryRemove(claim, out _);
            }
        }
        return complete;
    }
}
