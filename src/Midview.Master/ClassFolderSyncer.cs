using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Midview.Protocol;

namespace Midview.Master;

// How one machine's turn went, for the teacher.
public sealed record MachineSyncResult(string Name, bool Succeeded, string Detail);

// Keeps the lesson's "Pasta da turma" on every machine in step with the
// teacher's copy (ClassStore): a machine that doesn't have the lesson yet is
// loaded, and every Interval each one's new, changed and deleted files are
// saved to the copy. Machines run MachinesAtATime at once; the copy and the
// lesson are each changed under a lock of their own, and the lesson is
// written to its file after every round.
public sealed class ClassFolderSyncer : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    // Like sending and collecting files.
    private const int MachinesAtATime = 4;

    private readonly ClassStore _store;
    private readonly X509Certificate2 _masterCertificate;
    private readonly string _lessonPath;
    private readonly IRecycleBin _recycleBin;
    private readonly Func<IReadOnlyList<KnownMachine>> _machines;
    private readonly TimeSpan _interval;
    private readonly Func<KnownMachine, int> _port;
    private readonly object _storeGate = new();
    private readonly object _lessonGate = new();
    private readonly SemaphoreSlim _roundGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();

    // Machines whose Session predates the class folder: not asked again
    // until the teacher's program restarts (it would only answer its screen).
    private readonly HashSet<string> _olderSessions = new(StringComparer.OrdinalIgnoreCase);
    private Task? _loop;

    // machines: the machines to keep in step, asked before every round (on
    // the syncer's own thread) -- the ones that are on. port: each machine's
    // Session port, for tests; the usual one otherwise.
    public ClassFolderSyncer(
        ClassLesson lesson, ClassStore store, X509Certificate2 masterCertificate, string lessonPath,
        IRecycleBin recycleBin, Func<IReadOnlyList<KnownMachine>> machines,
        TimeSpan? interval = null, Func<KnownMachine, int>? port = null)
    {
        Lesson = lesson;
        _store = store;
        _masterCertificate = masterCertificate;
        _lessonPath = lessonPath;
        _recycleBin = recycleBin;
        _machines = machines;
        _interval = interval ?? DefaultInterval;
        _port = port ?? (_ => ConnectionClient.SessionPort);
    }

    public ClassLesson Lesson { get; }

    public ClassStore Store => _store;

    // After every round, on the syncer's thread: when it ended and how each
    // machine went.
    public event Action<DateTime, IReadOnlyList<MachineSyncResult>>? RoundFinished;

    // Rounds every Interval, the first one right away.
    public void Start()
    {
        _loop ??= Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    var results = await SyncNowAsync(_machines());
                    RoundFinished?.Invoke(DateTime.Now, results);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Class folder round failed: {ex.Message}");
                }
                try
                {
                    await Task.Delay(_interval, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    // Stops the rounds; the one running, if any, finishes first.
    public async Task StopAsync()
    {
        _stop.Cancel();
        if (_loop is { } loop)
        {
            await loop;
        }
    }

    // One round now, never alongside another.
    public async Task<IReadOnlyList<MachineSyncResult>> SyncNowAsync(IReadOnlyList<KnownMachine> machines)
    {
        await _roundGate.WaitAsync();
        try
        {
            using var slots = new SemaphoreSlim(MachinesAtATime);
            var results = await Task.WhenAll(machines.Select(async machine =>
            {
                await slots.WaitAsync();
                try
                {
                    return await Task.Run(() => Sync(machine));
                }
                finally
                {
                    slots.Release();
                }
            }));
            SaveLesson();
            return results;
        }
        finally
        {
            _roundGate.Release();
        }
    }

    // One machine: loaded if it doesn't hold the lesson yet, saved otherwise.
    public MachineSyncResult Sync(KnownMachine machine)
    {
        try
        {
            lock (_olderSessions)
            {
                if (_olderSessions.Contains(machine.SessionThumbprint))
                {
                    return new MachineSyncResult(machine.Name, false, CommandClient.OlderSession);
                }
            }

            List<ClassFolderEntry>? known;
            lock (_lessonGate)
            {
                Lesson.Machines.TryGetValue(machine.SessionThumbprint, out known);
            }
            if (known is null)
            {
                return Load(machine);
            }

            var result = Collect(machine, known);
            if (result.NotLoaded)
            {
                lock (_lessonGate)
                {
                    Lesson.Machines.Remove(machine.SessionThumbprint);
                }
                return Load(machine);
            }
            return Report(machine, result, result.Files == 0 ? "sem mudanças" : $"{result.Files} arquivo(s) salvo(s)");
        }
        catch (Exception ex)
        {
            return new MachineSyncResult(machine.Name, false, FailureText.Describe(ex));
        }
    }

    // "Apagar só os de hoje": what this lesson added to the copy.
    public (int Recycled, IReadOnlyList<string> Problems) RecycleAdded()
    {
        List<string> added;
        lock (_lessonGate)
        {
            added = Lesson.Added.ToList();
        }
        lock (_storeGate)
        {
            return _store.RecyclePaths(added, _recycleBin);
        }
    }

    // "Apagar tudo": the class's whole copy.
    public (int Recycled, IReadOnlyList<string> Problems) RecycleAll()
    {
        lock (_storeGate)
        {
            return _store.RecycleAll(_recycleBin);
        }
    }

    // The end of the lesson on the machines: every folder emptied.
    public static async Task<IReadOnlyList<MachineSyncResult>> ClearAsync(
        IReadOnlyList<KnownMachine> machines, X509Certificate2 masterCertificate, Func<KnownMachine, int>? port = null)
    {
        return await Task.WhenAll(machines.Select(machine => Task.Run(() =>
        {
            try
            {
                var (succeeded, detail) = ClassFolderClient.Clear(
                    machine, masterCertificate, port?.Invoke(machine) ?? ConnectionClient.SessionPort);
                return new MachineSyncResult(machine.Name, succeeded, detail);
            }
            catch (Exception ex)
            {
                return new MachineSyncResult(machine.Name, false, FailureText.Describe(ex));
            }
        })));
    }

    // The lesson is over: its file goes, so the next start asks for a class.
    public void Forget() => ClassLessonFile.Delete(_lessonPath);

    public void Dispose()
    {
        _stop.Cancel();
    }

    private MachineSyncResult Load(KnownMachine machine)
    {
        IReadOnlyList<ClassFolderFile> files;
        lock (_storeGate)
        {
            files = _store.ListFiles();
        }

        var result = ClassFolderClient.Load(machine, _masterCertificate, Lesson.Id, Lesson.ClassName, files, _port(machine));
        if (result.Listing is { } listing)
        {
            // Only what the machine has exactly as the copy does counts as
            // seen; anything else there is saved from it next time.
            var manifest = files.ToDictionary(file => file.Entry.Path, file => file.Entry, StringComparer.OrdinalIgnoreCase);
            var held = listing.Where(entry => manifest.TryGetValue(entry.Path, out var copy) && copy.SameVersionAs(entry)).ToList();
            lock (_lessonGate)
            {
                Lesson.Machines[machine.SessionThumbprint] = held;
            }
        }
        return Report(machine, result, $"pasta carregada ({result.Listing?.Count ?? 0} arquivo(s))");
    }

    private ClassFolderResult Collect(KnownMachine machine, List<ClassFolderEntry> known)
    {
        var knownByPath = known.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var received = new List<ClassFolderEntry>();
        var result = ClassFolderClient.Collect(
            machine, _masterCertificate, Lesson.Id, known, _store.StagingDirectory,
            (entry, tempPath) => Save(machine, knownByPath.GetValueOrDefault(entry.Path), entry, tempPath),
            received, _port(machine));
        if (result.Listing is not { } listing)
        {
            return result;
        }

        // What the machine no longer has was deleted (or renamed) there.
        var problems = new List<string>();
        var listed = new HashSet<string>(listing.Select(entry => entry.Path), StringComparer.OrdinalIgnoreCase);
        lock (_storeGate)
        {
            foreach (var gone in known.Where(entry => !listed.Contains(entry.Path)))
            {
                try
                {
                    _store.Remove(gone, _recycleBin);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    problems.Add($"{gone.Path} não saiu daqui ({ErrorText.Describe(ex)})");
                }
            }
        }

        // Seen now: what didn't change and what was saved. A changed file
        // that didn't arrive keeps the version seen before, so it's asked
        // for again next time.
        var receivedByPath = received.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var held = listing
            .Select(entry => knownByPath.TryGetValue(entry.Path, out var seen) && seen.SameVersionAs(entry)
                ? entry
                : receivedByPath.GetValueOrDefault(entry.Path) ?? knownByPath.GetValueOrDefault(entry.Path))
            .OfType<ClassFolderEntry>()
            .ToList();
        lock (_lessonGate)
        {
            Lesson.Machines[machine.SessionThumbprint] = held;
        }

        return problems.Count == 0
            ? result
            : result with { Detail = string.Join("; ", new[] { result.Detail }.Concat(problems).Where(part => part.Length > 0)) };
    }

    private string? Save(KnownMachine machine, ClassFolderEntry? seenBefore, ClassFolderEntry entry, string tempPath)
    {
        lock (_storeGate)
        {
            try
            {
                _store.Accept(entry, tempPath, seenBefore, machine.Name, created: path =>
                {
                    lock (_lessonGate)
                    {
                        Lesson.Added.Add(path);
                    }
                });
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
            {
                return $"{entry.Path} ({ErrorText.Describe(ex)})";
            }
        }
    }

    private MachineSyncResult Report(KnownMachine machine, ClassFolderResult result, string done)
    {
        if (result.Detail == CommandClient.OlderSession)
        {
            lock (_olderSessions)
            {
                _olderSessions.Add(machine.SessionThumbprint);
            }
        }
        if (!result.Succeeded && result.Listing is null)
        {
            return new MachineSyncResult(machine.Name, false, result.Detail);
        }
        return new MachineSyncResult(machine.Name, result.Succeeded, result.Detail.Length == 0 ? done : $"{done}; {result.Detail}");
    }

    private void SaveLesson()
    {
        lock (_lessonGate)
        {
            try
            {
                ClassLessonFile.Save(_lessonPath, Lesson);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Could not save the lesson to {_lessonPath}: {ex.Message}");
            }
        }
    }
}
