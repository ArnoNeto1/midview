using System.Threading.Channels;
using Midview.Protocol;

namespace Midview.Master;

// How this Master works the "Pasta da turma" ("Two teacher PCs in one lab",
// docs/superpowers/specs/2026-10-20-class-folder-design.md; plan decision 8,
// Rulings 1, 3, 9, 17 and 24): as the class folders' home, or as a remote
// control of the teacher PC that is. It goes by ClassHomeTracker's readings,
// acting on a change only once two in a row say it (ClassHomeConfirmation), or
// at once when the Agents name this PC as the home (its file read) or this PC
// handed the folders over itself (handOver.HandedOver), and keeps what goes
// with each mode:
//
// - Local (this PC is the home, or no PC is yet): the window works this PC's
//   own store and sync (local); its ClassFolderHost serves them to colleagues'
//   PCs (openHost: when the port can't be had, the host logs it and it is
//   tried again after HostRetryWait; the Master never opens the firewall); and
//   the pollers hand the lesson to the Agents (LessonForPollers) and take back
//   Agents that keep an older home (HomeGeneration), once a reading that asked
//   an Agent said Local, and only while the last one says so.
// - Remote (another PC is the home): the window works that PC's, through a
//   RemoteClassFolderController (newRemote); the host is closed; the pollers
//   hand nothing; and the toolbar shows the lesson that PC runs (ShownLesson:
//   as the room's Agents follow it, agentsLesson, or as the window last read
//   it from that PC, the newer). Going Remote, this PC stands down
//   (ClassFolderSync.StandDownForAnotherHomeAsync): never while a lesson of
//   its own is on that the room's Agents still follow, which keeps it Local,
//   holding, so that lesson can still be ended here (Ruling 17): its pollers
//   go on handing it out, and its end too for EndedGrace once it is over; a
//   lesson of its own the room left behind is ended first, on the PCs still
//   in it and here.
//
// While a take-over isn't over (handOver.IsBusy: one runs here, a hand-over or
// a take-back runs for a colleague's PC, or a copy sent hasn't settled), no
// reading changes anything: this PC neither stands down nor closes its host,
// which the PC taking the folders over must still reach (Ruling 24). A reading
// naming this PC the home above the generation it handed the folders over at
// voids that hand-over (handOver.SeenAsHome).
//
// Readings are acted on one at a time, in order, on a thread of their own.
// A change not confirmed yet asks for another reading after confirmWait, and
// a take-over or a hand-over over (handOver.Changed) for one at once.
// Changed is raised (on a background thread) when the mode, the controller or
// the lesson shown may have changed. A remote controller given up is stopped
// but kept, as a window may still hold it, until ReleaseRetired. The role owns
// the tracker, the host and the remote controllers: Dispose disposes them. In
// two files: this one (what the Master reads of it) and ClassFolderRole.Acting.cs
// (the readings acted on).
public sealed partial class ClassFolderRole : IDisposable
{
    public static readonly TimeSpan DefaultConfirmWait = TimeSpan.FromSeconds(3);

    // How long the end of a lesson this PC held the folders for is still handed
    // out (Ruling 17): the pollers' wait after a refusal, and a minute of polls.
    public static readonly TimeSpan DefaultEndedGrace = ThumbnailPoller.DefaultAgentRetryInterval + TimeSpan.FromMinutes(1);

    // After the host couldn't listen, how soon it is tried again.
    public static readonly TimeSpan DefaultHostRetryWait = TimeSpan.FromSeconds(5);

    // After the first machine of the run answers, how long until the room is
    // asked: a few more answer meanwhile, and are asked too.
    private static readonly TimeSpan AnsweredWait = TimeSpan.FromSeconds(1);

    // How long Dispose waits for the reading being acted on (a stand-down waits for the PCs' rounds).
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(5);

    private readonly ClassHomeTracker _tracker;
    private readonly ClassFolderSync _sync;
    private readonly IClassFolderController _local;
    private readonly ClassFolderHandOver? _handOver;
    private readonly Func<ClassFolderHost?> _openHost;
    private readonly Func<RemoteClassFolderController> _newRemote;
    private readonly Func<ClassLesson?> _agentsLesson;
    private readonly TimeSpan _confirmWait;
    private readonly Action<string> _log;

    // Only the acting thread uses it.
    private readonly ClassHomeConfirmation _confirmation = new();

    // The readings to act on, and markers (a null reading) the tests wait on.
    private readonly Channel<(ClassHomeReading? Reading, TaskCompletionSource? Done)> _readings =
        Channel.CreateUnbounded<(ClassHomeReading?, TaskCompletionSource?)>(new UnboundedChannelOptions { SingleReader = true });

    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acting;

    // What the other threads read. Under _lock.
    private readonly object _lock = new();
    private readonly List<RemoteClassFolderController> _retired = new();
    private ClassFolderMode? _mode;
    private ClassFolderHost? _host;
    private RemoteClassFolderController? _remote;
    private ClassHomeReading? _latest;
    private bool _lessonsOpen;
    private ClassLesson? _homesLesson;
    private bool _disposed;

    // Local only for a lesson of this PC's the room still follows (Ruling 17),
    // and, once it ended, until when its end is still handed out
    // (Environment.TickCount64; null: not ended yet). Under _lock.
    private bool _holding;
    private long? _graceUntil;

    // The last reading was held for a take-over not over yet (only the acting thread uses it).
    private bool _heldForHandOver;

    // When the next refresh asked for (RefreshSoon) begins (Environment.TickCount64;
    // long.MaxValue: none waits). Under _lock.
    private long _refreshDue = long.MaxValue;

    // Whether any reading asked an Agent.
    private volatile bool _askedAny;

    // tracker: this PC's (its Refreshed is what the role goes by). sync and
    // local: this PC's own store's. handOver: this PC's, whose take-overs and
    // hand-overs make the role ask the room again. openHost: this PC's
    // ClassFolderHost over local (null when the port can't be had). newRemote:
    // a controller of the home the tracker names. agentsLesson: the newest
    // lesson the room's online Agents follow (blocking; null for none).
    // confirmWait: DefaultConfirmWait. log: in English, by default to
    // System.Diagnostics.Trace.
    public ClassFolderRole(
        ClassHomeTracker tracker, ClassFolderSync sync, IClassFolderController local, ClassFolderHandOver? handOver,
        Func<ClassFolderHost?> openHost, Func<RemoteClassFolderController> newRemote, Func<ClassLesson?> agentsLesson,
        TimeSpan? confirmWait = null, Action<string>? log = null)
    {
        if (confirmWait <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmWait));
        }
        _tracker = tracker;
        _sync = sync;
        _local = local;
        _handOver = handOver;
        _openHost = openHost;
        _newRemote = newRemote;
        _agentsLesson = agentsLesson;
        _confirmWait = confirmWait ?? DefaultConfirmWait;
        _log = log ?? (message => System.Diagnostics.Trace.WriteLine("ClassFolderRole: " + message));

        _tracker.Refreshed += OnRefreshed;
        if (_handOver is not null)
        {
            _handOver.Changed += OnHandOverChanged;
        }
        _acting = Task.Run(ActAsync);
    }

    public event Action? Changed;

    // The mode acted on; null before the first reading decided one.
    public ClassFolderMode? Mode
    {
        get
        {
            lock (_lock)
            {
                return _mode;
            }
        }
    }

    // What the window works: the home's (remote) in Remote mode, this PC's own otherwise.
    public IClassFolderController Controller
    {
        get
        {
            lock (_lock)
            {
                return (IClassFolderController?)_remote ?? _local;
            }
        }
    }

    // The host while it listens.
    public ClassFolderHost? Host
    {
        get
        {
            lock (_lock)
            {
                return _host;
            }
        }
    }

    // What the pollers hand the Agents (null: nothing).
    public ClassLesson? LessonForPollers
    {
        get
        {
            lock (_lock)
            {
                return HandsOutLocked() ? _sync.Lesson : null;
            }
        }
    }

    // Whether what the pollers hand out is the end of a lesson this PC held for
    // the room while another PC is, or may be, the home
    // (ClassFolderSync.EndGoesOnlyToItsLesson): only an Agent still in it gets it.
    public bool HandsOutAHeldEnd => _sync.EndGoesOnlyToItsLesson;

    // For a lesson an Agent follows, the end the pollers hand it when this PC
    // knows that lesson is over (ClassFolderSync.EndOfLessonOver), while they
    // hand lessons out; null otherwise.
    public ClassLesson? EndOfLessonOver(ClassLesson followed, ClassHome? keptHome)
    {
        lock (_lock)
        {
            if (!HandsOutLocked())
            {
                return null;
            }
        }
        return _sync.EndOfLessonOver(followed, keptHome);
    }

    // This PC's generation as the home, which the pollers claim stragglers
    // with (null while it isn't the home, or doesn't hand anything out, or only
    // holds the folders for its lesson): a Local reading with a home is this
    // PC's, at the newest generation the room keeps for it.
    public long? HomeGeneration
    {
        get
        {
            lock (_lock)
            {
                return HandsOutLocked() && _latest is { Mode: ClassFolderMode.Local, Home: { } home } ? home.Generation : null;
            }
        }
    }

    // DefaultEndedGrace and DefaultHostRetryWait, or a test's.
    internal TimeSpan EndedGrace { get; init; } = DefaultEndedGrace;

    internal TimeSpan HostRetryWait { get; init; } = DefaultHostRetryWait;

    // The lesson under the toolbar button: this PC's own, or, Remote, the home's.
    public ClassLesson ShownLesson
    {
        get
        {
            ClassLesson? agents;
            RemoteClassFolderController? remote;
            lock (_lock)
            {
                if (_mode != ClassFolderMode.Remote)
                {
                    return _sync.Lesson;
                }
                (agents, remote) = (_homesLesson, _remote);
            }
            var window = remote?.Lesson;
            return (window is not null && (agents is null || window.SavedAt > agents.SavedAt) ? window : agents) ?? ClassLesson.None(0);
        }
    }

    // Asks the room now and acts on what it says (once the role's turn comes).
    public void Start() => RefreshSoon(TimeSpan.Zero);

    // The room asked again after a while (now by default); nothing when it
    // will be asked by then anyway.
    public void RefreshSoon(TimeSpan? after = null)
    {
        var wait = after ?? TimeSpan.Zero;
        var due = Environment.TickCount64 + (long)wait.TotalMilliseconds;
        lock (_lock)
        {
            if (_disposed || _refreshDue <= due)
            {
                return;
            }
            _refreshDue = due;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(wait, _stop.Token).ConfigureAwait(false);
                lock (_lock)
                {
                    if (_refreshDue == due)
                    {
                        _refreshDue = long.MaxValue;
                    }
                }
                await _tracker.RefreshAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Disposed.
            }
            catch (Exception ex)
            {
                _log($"Asking which PC keeps the class folders failed: {ex}");
            }
        });
    }

    // A machine answered its poller: until a reading has asked an Agent, the room is asked again soon.
    public void MachineAnswered()
    {
        if (!_askedAny)
        {
            RefreshSoon(AnsweredWait);
        }
    }

    // The remote controllers given up, which no window holds any more, are disposed.
    public void ReleaseRetired()
    {
        List<RemoteClassFolderController> retired;
        lock (_lock)
        {
            retired = _retired.ToList();
            _retired.Clear();
        }
        retired.ForEach(remote => remote.Dispose());
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }
        _tracker.Refreshed -= OnRefreshed;
        if (_handOver is not null)
        {
            _handOver.Changed -= OnHandOverChanged;
        }
        _stop.Cancel();
        _readings.Writer.TryComplete();
        try
        {
            _acting.Wait(DisposeWait);
        }
        catch (AggregateException)
        {
            // The acting loop catches everything; nothing to report here.
        }
        _tracker.Dispose();

        ClassFolderHost? host;
        List<RemoteClassFolderController> remotes;
        lock (_lock)
        {
            host = _host;
            _host = null;
            remotes = _retired.Append(_remote).OfType<RemoteClassFolderController>().ToList();
            _retired.Clear();
            _remote = null;
        }
        host?.Dispose();
        remotes.ForEach(remote => remote.Dispose());
    }

    // Under _lock. Also while holding for a lesson the room still follows.
    private bool HandsOutLocked() =>
        _mode == ClassFolderMode.Local && _lessonsOpen && (_latest?.Mode == ClassFolderMode.Local || _holding);

    // What Changed is about.
    private (ClassFolderMode?, IClassFolderController, ClassLesson) Seen() => (Mode, Controller, ShownLesson);

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _log($"A Changed handler failed: {ex}");
        }
    }
}
