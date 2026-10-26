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

    // For the tests: whether a refresh asked for (RefreshSoon) is still to begin.
    internal bool RefreshPending
    {
        get
        {
            lock (_lock)
            {
                return _refreshDue != long.MaxValue;
            }
        }
    }

    // For the tests: a reading acted on, as one from the tracker would be.
    internal async Task ActOnAsync(ClassHomeReading reading)
    {
        OnRefreshed(reading);
        await ActedOnAsync().ConfigureAwait(false);
    }

    // Once every reading before it is acted on (and for the tests).
    internal Task ActedOnAsync()
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

        // The room names this PC the home: a hand-over of its at an older generation is behind it.
        if (reading is { Mode: ClassFolderMode.Local, Home: { } named, FileRead: true })
        {
            _handOver?.SeenAsHome(named.Generation);
        }

        if (_handOver is { } handOver && (handOver.IsBusy || reading.StartedAt < handOver.QuietSince))
        {
            // A take-over runs, here or for a colleague's PC, or a copy
            // sent hasn't settled: its answer must find this PC as it is (its host
            // open). Nothing changes on a reading, nor on one begun before that was
            // over (what the room said meanwhile); the hand-over says when it is
            // over (Changed), and the room is asked again then.
            if (!_heldForHandOver)
            {
                _log("A take-over of the class folders isn't over yet: this PC stays as it is until it is.");
            }
            _heldForHandOver = true;
        }
        else
        {
            _heldForHandOver = false;
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
        }

        if (_confirmation.Acted == ClassFolderMode.Local)
        {
            lock (_lock)
            {
                if (reading.Mode == ClassFolderMode.Local)
                {
                    // The room names this PC (or none) again: nothing held any more.
                    _holding = false;
                }
                _lessonsOpen |= reading.Asked > 0 && (reading.Mode == ClassFolderMode.Local || _holding);
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
            _graceEnd = null;
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
    // stays Local (or becomes it, the first time), holding: its pollers go on
    // handing that lesson out (the Agents take a lesson set only from the home
    // they keep), until it ends. Then it goes Remote at once, and
    // that lesson's end (or a start's backed out) goes on being handed out for
    // EndedGrace, only to the PCs still in it: those that were busy or off.
    private async Task GoRemoteAsync()
    {
        var lesson = _sync.Lesson;
        var heldEnd = !lesson.IsOn && _sync.IsHeldEnd(lesson) ? lesson : null;
        if (await _sync.StandDownForAnotherHomeAsync().ConfigureAwait(false) is { } on)
        {
            if (!Holding())
            {
                _log($"Another PC keeps the class folders, but the lesson of {on.Turma} is still on here: this PC keeps them until it ends.");
            }
            lock (_lock)
            {
                _holding = true;
            }
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
            _holding = false;
            _graceEnd = heldEnd;
            _graceUntil = Milliseconds() + (long)EndedGrace.TotalMilliseconds;
        }
        host?.Dispose();
        _hostFailures = 0;
        _confirmation.Acting(ClassFolderMode.Remote);
        _log($"Another PC keeps the class folders ({_latest?.Home?.Name}): this PC works them through it.");
        if (heldEnd is not null)
        {
            _log($"The end of the lesson of {heldEnd.Turma} is still handed out for {EndedGrace}, to the PCs still in it.");
        }
    }

    // How many times in a row the host couldn't listen (only the acting thread uses it).
    private int _hostFailures;

    // Only while this PC acts Local (a remote has nothing to serve); logged by
    // the host when the port can't be had, and tried again after HostRetryWait
    // (the port a host closed a moment ago, after a quick switch back, may not
    // be free yet), up to HostQuickTries times; after that (another program
    // holds the port, say) at each of the room's readings.
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
        if (host is null)
        {
            if (++_hostFailures <= HostQuickTries)
            {
                RefreshSoon(HostRetryWait);
            }
            return;
        }
        _hostFailures = 0;
        lock (_lock)
        {
            if (_host is null && _mode == ClassFolderMode.Local && !_disposed)
            {
                _host = host;
                return;
            }
        }
        host.Dispose();
    }

    private bool Holding()
    {
        lock (_lock)
        {
            return _holding;
        }
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
