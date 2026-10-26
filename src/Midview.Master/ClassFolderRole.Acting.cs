using Midview.Protocol;

namespace Midview.Master;

// ClassFolderRole: acting on the readings, one at a time, in order, on a
// thread of its own, and what each mode brings with it.
public sealed partial class ClassFolderRole
{
    // For the tests: the room asked, and what it said acted on.
    internal async Task RefreshAndActAsync()
    {
        await Task.Run(_tracker.RefreshAsync).ConfigureAwait(false);
        await ActedOnAsync().ConfigureAwait(false);
    }

    // For the tests: a reading acted on, as one from the tracker would be.
    internal async Task ActOnAsync(ClassHomeReading reading)
    {
        OnRefreshed(reading);
        await ActedOnAsync().ConfigureAwait(false);
    }

    // Once every reading before it is acted on.
    private Task ActedOnAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _readings.Writer.TryWrite((null, done)) ? done.Task : Task.CompletedTask;
    }

    private void OnRefreshed(ClassHomeReading reading) => _readings.Writer.TryWrite((reading, null));

    private void OnHandOverChanged() => RefreshSoon();

    private async Task ActAsync()
    {
        try
        {
            await foreach (var (reading, done) in _readings.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                if (reading is not null)
                {
                    try
                    {
                        await ActOnReadingAsync(reading).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _log($"Acting on which PC keeps the class folders failed: {ex}");
                    }
                }
                done?.TrySetResult();
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }

    private async Task ActOnReadingAsync(ClassHomeReading reading)
    {
        var before = Seen();
        if (reading.Asked > 0)
        {
            _askedAny = true;
        }
        lock (_lock)
        {
            _latest = reading;
        }

        // This PC handed the folders over itself: another PC keeps them, no reading gone wrong.
        var sure = reading.Mode == ClassFolderMode.Remote && _handOver?.HandedOver == true;
        switch (_confirmation.Take(reading, sure))
        {
            case ClassFolderMode.Local:
                GoLocal();
                break;
            case ClassFolderMode.Remote:
                await GoRemoteAsync().ConfigureAwait(false);
                break;
            default:
                if (_confirmation.Confirming)
                {
                    RefreshSoon(_confirmWait);
                }
                break;
        }

        if (_confirmation.Acted == ClassFolderMode.Local)
        {
            lock (_lock)
            {
                _lessonsOpen |= reading.Mode == ClassFolderMode.Local && reading.Asked > 0;
            }
            OpenHostIfClosed();
        }
        else if (_confirmation.Acted == ClassFolderMode.Remote)
        {
            var lesson = await Task.Run(ReadAgentsLesson).ConfigureAwait(false);
            lock (_lock)
            {
                _homesLesson = lesson ?? _homesLesson;
            }
        }

        if (!Seen().Equals(before))
        {
            RaiseChanged();
        }
    }

    private void GoLocal()
    {
        RemoteClassFolderController? retired;
        lock (_lock)
        {
            retired = _remote;
            _remote = null;
            if (retired is not null)
            {
                _retired.Add(retired);
            }
            _mode = ClassFolderMode.Local;
            _homesLesson = null;
        }
        if (retired is not null)
        {
            retired.Changed -= RaiseChanged;
            retired.Stop();
        }
        _confirmation.Acting(ClassFolderMode.Local);
        _log("This PC keeps the class folders, or no PC does yet: it works them itself.");
    }

    // Never while a lesson of this PC's is on that the room still follows: it
    // stays Local (or becomes it, the first time) until that lesson ends.
    private async Task GoRemoteAsync()
    {
        if (await _sync.StandDownForAnotherHomeAsync().ConfigureAwait(false) is { } on)
        {
            _log($"Another PC keeps the class folders, but the lesson of {on.Turma} is still on here: this PC keeps them until it ends.");
            if (_confirmation.Acted is null)
            {
                GoLocal();
            }
            return;
        }

        var remote = _newRemote();
        remote.Changed += RaiseChanged;
        ClassFolderHost? host;
        lock (_lock)
        {
            host = _host;
            _host = null;
            _remote = remote;
            _mode = ClassFolderMode.Remote;
            _lessonsOpen = false;
        }
        host?.Dispose();
        _confirmation.Acting(ClassFolderMode.Remote);
        _log($"Another PC keeps the class folders ({_latest?.Home?.Name}): this PC works them through it.");
    }

    // Ruling 3: only while this PC acts Local; logged by the host when the port can't be had.
    private void OpenHostIfClosed()
    {
        lock (_lock)
        {
            if (_host is not null || _disposed)
            {
                return;
            }
        }
        var host = _openHost();
        lock (_lock)
        {
            if (_host is null && _mode == ClassFolderMode.Local && !_disposed)
            {
                _host = host;
                return;
            }
        }
        host?.Dispose();
    }

    private ClassLesson? ReadAgentsLesson()
    {
        try
        {
            return _agentsLesson();
        }
        catch (Exception ex)
        {
            _log($"Reading the lesson the room follows failed: {ex}");
            return null;
        }
    }
}
