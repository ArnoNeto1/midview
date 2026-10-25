using System.IO;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// The "Pasta da turma" window's controller on a colleague's teacher PC: the
// class folders' home is another teacher PC, whose Master serves its own
// window (ClassFolderHost; "Two teacher PCs in one lab",
// docs/superpowers/specs/2026-10-20-class-folder-design.md, plan decisions 6
// and 9). The home is where the room's Agents say (home: ClassHomeTracker.Home,
// looked at again for every request, as its address can change), reached over
// TLS with its certificate pinned.
//
// While the window is open (Start and Stop, MainWindow's to call) the home's
// state is asked for every pollInterval (2 s), one request at a time, the next
// only once the last is over; the window's own reads take the last state when
// it is fresh, or wait their turn. The teacher's commands go one at a time,
// each on its own connection, and each is followed by a fresh state before it
// returns (the window looks at Lesson right after a start). A connection the
// home turns away before the request goes (its cap on one PC's connections) is
// tried again shortly.
//
// The home is closed (HomeClosed) only once ClosedAfter polls found nobody
// there, with nothing but neutral polls between them: the connection refused
// or never taken (its Master closed, the PC off, or the firewall rule of its
// setup missing). Only the home's answer (a state, or a refusal) opens it
// again. A poll that connects but gets no answer (turned away, a handshake
// cut off, a home that hangs) is neutral: neither for nor against. So is a PC
// at the home's address that answers with another certificate than the one
// pinned, which Note says (Midview reinstalled there?). Closed, Lesson is the
// lesson the room's online Agents follow, and only "Encerrar a aula", keeping
// the files, works: straight on the Agents (RemoteClassFolderController.
// Closed.cs). Anything else is refused with ClassFolderText.HomeClosed.
//
// A home that answers a state with a refusal: one that can't read its class
// folders just now (ClassFolderRemote.UnreadableText) leaves the last view as
// it was, with its sentence as Note; any other (NotTrustedText: it doesn't take
// this PC's requests) leaves nothing of the home shown, with its sentence as Note.
//
// Changed is raised on background threads, never while a lock is held. machines
// and isOnline (this PC's classroom list and grid) are called from background
// threads too. In four files: this one (the home's state),
// RemoteClassFolderController.Commands.cs (the teacher's commands, and each
// request on the wire), RemoteClassFolderController.Closed.cs (the home closed)
// and RemoteClassFolderController.TakeOver.cs (the folders taken over).
public sealed partial class RemoteClassFolderController : IClassFolderController, IDisposable
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    // Polls in a row that found nobody at the home before it counts as closed.
    public const int ClosedAfter = 3;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // For a state: the home's deadline for the handshake and the request
    // (10 s), whether this PC is a colleague (up to 10 s), and its read (about
    // 10 s), with room to spare.
    private static readonly TimeSpan StateWait = TimeSpan.FromSeconds(40);

    // For a command: the home waits up to 5 minutes for it (a lesson's end
    // brings the last files in first), then says it is still running. TCP
    // keep-alive notices a home switched off meanwhile.
    private static readonly TimeSpan CommandWait = TimeSpan.FromMinutes(6);

    // A state younger than the poll interval and this is what the window's
    // reads get: the loop keeps it that fresh, so they add no requests.
    private static readonly TimeSpan FreshMargin = TimeSpan.FromSeconds(1);

    // A request the home turned away is sent this many times in all, this far apart.
    private const int Attempts = 3;
    private static readonly TimeSpan AttemptPause = TimeSpan.FromMilliseconds(150);

    private static readonly Message StateRequest = new(MessageType.RemoteClassFolderState, Array.Empty<byte>());

    private readonly X509Certificate2 _certificate;
    private readonly Func<ClassHome?> _home;
    private readonly Func<IReadOnlyList<KnownMachine>> _machines;
    private readonly Func<KnownMachine, bool> _isOnline;
    private readonly TimeSpan _pollInterval;
    private readonly int _agentPort;
    private readonly Func<DateTime> _localNow;
    private readonly Action<string> _log;

    // One state request at a time, and one command.
    private readonly SemaphoreSlim _polling = new(1, 1);
    private readonly SemaphoreSlim _commands = new(1, 1);

    // What the window is shown. Under _lock.
    private readonly object _lock = new();
    private RemoteState? _state;
    private byte[]? _signature;
    private long _stateAt;
    private string? _note;
    private int _unreachable;
    private bool _closed;

    // The polling loop while the window is open. Under _runLock.
    private readonly object _runLock = new();
    private CancellationTokenSource? _running;
    private bool _disposed;

    // masterCertificate: this teacher PC's. home: the class folders' home as
    // the room says (null: none known). agentPort and localNow: for the end
    // made on the Agents (ConnectionClient.AgentPort, DateTime.Now). log: what
    // went wrong that the window doesn't show, in English, by default to
    // System.Diagnostics.Trace.
    public RemoteClassFolderController(
        X509Certificate2 masterCertificate, Func<ClassHome?> home, Func<IReadOnlyList<KnownMachine>> machines,
        Func<KnownMachine, bool> isOnline, TimeSpan? pollInterval = null, int agentPort = ConnectionClient.AgentPort,
        Func<DateTime>? localNow = null, Action<string>? log = null)
    {
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        _certificate = masterCertificate;
        _home = home;
        _machines = machines;
        _isOnline = isOnline;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _agentPort = agentPort;
        _localNow = localNow ?? (() => DateTime.Now);
        _log = log ?? (message => System.Diagnostics.Trace.WriteLine("RemoteClassFolderController: " + message));
    }

    // The lesson the home has (the Agents' with the home closed); ClassLesson.None(0) before any is known.
    public ClassLesson Lesson
    {
        get
        {
            lock (_lock)
            {
                return LessonLocked();
            }
        }
    }

    public IReadOnlyList<PcLineDto> Pcs
    {
        get
        {
            lock (_lock)
            {
                return _state?.Pcs ?? Array.Empty<PcLineDto>();
            }
        }
    }

    // The home's; none with the home closed (an end made on the Agents does nothing to its files).
    public IReadOnlyList<string> LastEndProblems
    {
        get
        {
            lock (_lock)
            {
                return !_closed ? _state?.LastEndProblems ?? Array.Empty<string>() : Array.Empty<string>();
            }
        }
    }

    // As the room's Agents name it, else as it named itself, else "de outro professor".
    public string? HomeName
    {
        get
        {
            if (CurrentHome()?.Name is { Length: > 0 } name)
            {
                return name;
            }
            lock (_lock)
            {
                return _state?.HomeName is { Length: > 0 } itself ? itself : ClassHome.UnnamedText;
            }
        }
    }

    public bool HomeClosed
    {
        get
        {
            lock (_lock)
            {
                return _closed;
            }
        }
    }

    public string? Note
    {
        get
        {
            lock (_lock)
            {
                return _note;
            }
        }
    }

    public event Action? Changed;

    // Asks the home for its state now and every pollInterval after, until Stop.
    public void Start()
    {
        lock (_runLock)
        {
            if (_running is not null || _disposed)
            {
                return;
            }
            var running = new CancellationTokenSource();
            _running = running;
            _ = Task.Run(() => LoopAsync(running.Token));
        }
    }

    // A request already on its way finishes; no other starts.
    public void Stop()
    {
        lock (_runLock)
        {
            StopLocked();
        }
    }

    // Stops the loop. A request on its way finishes (a command's reply is
    // still returned); any call after this throws ObjectDisposedException.
    public void Dispose()
    {
        lock (_runLock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            StopLocked();
        }
        _polling.Dispose();
        _commands.Dispose();
    }

    // The last state when it is fresh, else a new one. Throws IOException when
    // there is none to give (the home closed, refusing, or not answering).
    public async Task<RemoteState> ReadAsync(bool lessonFiles)
    {
        lock (_lock)
        {
            if (!_closed && _note is null && _state is { } state
                && Environment.TickCount64 - _stateAt < (_pollInterval + FreshMargin).TotalMilliseconds)
            {
                return state;
            }
        }
        if (HomeClosed)
        {
            throw new IOException(ClosedText);
        }
        return await PollOnceAsync().ConfigureAwait(false) ?? throw new IOException(Note ?? ClassFolderText.HomeNoAnswer(Name));
    }

    // One state from the home, after any other request for one, and what it changes (for the tests).
    internal Task PollAsync() => PollOnceAsync();

    private string Name => HomeName ?? ClassHome.UnnamedText;

    private string ClosedText => ClassFolderText.HomeClosed(Name);

    private ClassLesson LessonLocked() => _closed && _agentsLesson is { } agents ? agents : HomesLessonLocked();

    private void StopLocked()
    {
        if (_running is { } running)
        {
            running.Cancel();
            running.Dispose();
            _running = null;
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await PollOnceAsync().ConfigureAwait(false);
            try
            {
                await Task.Delay(_pollInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // The state the home gave, or null. Never throws: what went wrong is the
    // view's to show, or logged; and whatever it changed is told (Changed).
    private async Task<RemoteState?> PollOnceAsync()
    {
        try
        {
            await _polling.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        View before, after;
        RemoteState? state = null;
        try
        {
            before = Snapshot();
            try
            {
                var exchanged = CurrentHome() is { } home
                    ? await RequestAsync(home, StateRequest, StateWait).ConfigureAwait(false)
                    : new Exchanged(Kind.Unreachable, null);
                var otherIdentity = exchanged.Kind == Kind.WrongIdentity ? ClassFolderText.HomeOtherIdentity(Name) : null;
                state = Take(exchanged, otherIdentity);
                if (AgentsLessonDue())
                {
                    await ReadAgentsLessonAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log($"Asking the class folders' home for its state failed: {ex}");
            }
            after = Snapshot();
        }
        finally
        {
            Release(_polling);
        }

        if (!before.IsSameAs(after))
        {
            RaiseChanged();
        }
        return state;
    }

    // What one state request found, kept for the window; the state, or null.
    // otherIdentity: what Note says for a PC with another certificate.
    private RemoteState? Take(Exchanged exchanged, string? otherIdentity)
    {
        lock (_lock)
        {
            switch (exchanged.Kind)
            {
                case Kind.Unreachable:
                    if (++_unreachable >= ClosedAfter && !_closed)
                    {
                        // The Agents are asked at once (AgentsLessonDue); until then, the lesson the home last had.
                        _closed = true;
                        _agentsLesson = null;
                        _agentsReadAt = null;
                    }
                    return null;

                case Kind.WrongIdentity:
                    // Neutral, but said: that PC is not the home this PC knows.
                    _note = otherIdentity;
                    return null;

                case Kind.TurnedAway or Kind.Lost:
                    return null;
            }

            var reply = exchanged.Reply;
            if (reply?.Type == MessageType.RemoteClassFolderStateReply && ClassFolderRemote.DecodeJson<RemoteState>(reply.Payload) is { } state)
            {
                Answered();
                _state = state;
                _signature = ClassFolderRemote.EncodeJson(state with { FreeBytes = 0, RecycleBinBytes = 0 });
                _stateAt = Environment.TickCount64;
                _note = null;
                if (_endedHere is { } ended && (ended.LessonId != state.Lesson.LessonId || state.Lesson.SavedAt >= ended.SavedAt))
                {
                    _endedHere = null; // the home has taken that end in, or moved on
                }
                return state;
            }
            if (reply?.Type == MessageType.CommandAck && CommandResult.Decode(reply.Payload).Detail is { Length: > 0 } refusal)
            {
                Answered();
                _note = refusal;
                if (!refusal.StartsWith(ClassFolderRemote.UnreadableText, StringComparison.Ordinal))
                {
                    // Not taken, or not understood: nothing of the home is this PC's to show.
                    _state = null;
                    _signature = null;
                }
            }
            // Otherwise (an answer that is neither) the view stays as it was.
            return null;
        }
    }

    // The home's Master answered: it is there. Under _lock.
    private void Answered()
    {
        _unreachable = 0;
        _closed = false;
    }

    // A command or a poll still on its way when the controller was disposed.
    private static void Release(SemaphoreSlim gate)
    {
        try
        {
            gate.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private ClassHome? CurrentHome()
    {
        try
        {
            return _home();
        }
        catch (Exception ex)
        {
            _log($"Asking which PC keeps the class folders failed: {ex}");
            return null;
        }
    }

    private View Snapshot()
    {
        lock (_lock)
        {
            return new View(_closed, _note, LessonLocked(), _signature);
        }
    }

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

    // What the window shows of the home, to tell whether anything changed (the
    // free space, which moves all the time, left out of the state's signature).
    private sealed record View(bool Closed, string? Note, ClassLesson Lesson, byte[]? Signature)
    {
        public bool IsSameAs(View other) =>
            Closed == other.Closed && Note == other.Note && Lesson == other.Lesson
            && (Signature is null ? other.Signature is null : other.Signature is not null && Signature.AsSpan().SequenceEqual(other.Signature));
    }
}
