using System.IO;
using System.Net.Sockets;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderSync: the loop that runs the rounds, and what is around them.
public sealed partial class ClassFolderSync
{
    private async Task LoopAsync()
    {
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, token).ConfigureAwait(false);
                await TickAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log($"The class folder loop failed: {ex}");
            }
        }
        await RunningRounds().ConfigureAwait(false);
    }

    // A round for every online PC whose last one is over.
    private async Task TickAsync(CancellationToken token)
    {
        var lesson = Lesson;
        if (!lesson.IsOn || _changingLesson)
        {
            return;
        }

        if (lesson.StartedBefore(_localNow()))
        {
            // The Master was left open overnight.
            await _lessonGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (Lesson == lesson)
                {
                    _changingLesson = true;
                    await RunningRounds().ConfigureAwait(false);
                    EndNobodyEnded(lesson);
                }
            }
            finally
            {
                _changingLesson = false;
                _lessonGate.Release();
            }
            return;
        }

        foreach (var machine in Machines())
        {
            if (!IsOnline(machine))
            {
                Report(machine, PcSyncState.Offline, string.Empty);
                continue;
            }

            var key = Key(machine);
            if (_loopRounds.TryGetValue(key, out var running) && !running.IsCompleted)
            {
                continue;
            }
            _loopRounds[key] = TrackedAsync(() => GatedRoundAsync(machine, lesson, collectOnly: false, token));
        }
    }

    private async Task LastRoundsAsync(ClassLesson lesson, CancellationToken token)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass > 0)
            {
                await Task.Delay(_interval < EndSettle ? _interval : EndSettle, token).ConfigureAwait(false);
            }
            var online = Machines().Where(IsOnline).ToList();
            await Task.WhenAll(online.Select(machine => GatedRoundAsync(machine, lesson, collectOnly: true, token)))
                .ConfigureAwait(false);
        }
    }

    // Waits for the rounds in progress. A start or an end sets _changingLesson
    // first: a round tracked after this looks has not got past its check yet, and
    // will see the flag and do nothing.
    private async Task RunningRounds() =>
        await Task.WhenAll(_rounds.Values.ToArray()).ConfigureAwait(false);

    // Tracked from before it begins until it is over, whatever becomes of it
    // (each round says its own trouble; a cancelled one is Dispose's).
    private async Task TrackedAsync(Func<Task> round)
    {
        var id = Interlocked.Increment(ref _lastRoundId);
        var over = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _rounds[id] = over.Task;
        try
        {
            await round().ConfigureAwait(false);
        }
        finally
        {
            _rounds.TryRemove(id, out _);
            over.TrySetResult();
        }
    }

    // Never two rounds of one PC at once.
    private async Task GatedRoundAsync(KnownMachine machine, ClassLesson lesson, bool collectOnly, CancellationToken token)
    {
        if (!lesson.IsOn)
        {
            return;
        }

        var gate = _pcGates.GetOrAdd(Key(machine), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // A round that a start or an end caught before it began has nothing to do:
            // only that change's own last rounds (collectOnly) go on.
            if (!collectOnly && (_changingLesson || Lesson.LessonId != lesson.LessonId))
            {
                return;
            }
            await RoundAsync(machine, lesson, collectOnly, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ex is not (SocketException or IOException or OperationCanceledException))
            {
                _log($"The round with {machine.Name} failed: {ex}");
            }
            var state = ex is SocketException or OperationCanceledException ? PcSyncState.Offline : PcSyncState.Failed;
            // What the PC answered that can't be gone by (a listing cut short, say) says so in the teacher's words.
            Report(machine, state, ex is InvalidDataException ? ex.Message : FailureText.Describe(ex));
        }
        finally
        {
            gate.Release();
        }
    }

    // --- Around the callers' functions ---

    private IReadOnlyList<KnownMachine> Machines()
    {
        try
        {
            return _machines();
        }
        catch (Exception ex)
        {
            _log($"The list of machines could not be read: {ex.Message}");
            return Array.Empty<KnownMachine>();
        }
    }

    private bool IsOnline(KnownMachine machine)
    {
        try
        {
            return _isOnline(machine);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Key(KnownMachine machine) => machine.SessionThumbprint;

    private void Report(KnownMachine machine, PcSyncState state, string detail)
    {
        var status = new PcStatus(machine, state, detail);
        var key = Key(machine);
        if (_statuses.TryGetValue(key, out var last) && last == status)
        {
            return;
        }

        _statuses[key] = status;
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception ex)
        {
            _log($"A StatusChanged handler failed: {ex}");
        }
    }
}
