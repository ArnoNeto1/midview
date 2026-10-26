using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// Keeps one preview connection open to one machine and asks it for a
// thumbnail every few seconds, on a thread of its own: one slow or offline
// machine never delays any other machine's thumbnail. The connection stays
// open between polls -- a full TLS handshake every few seconds, times 25+
// machines, would be needless overhead.
public sealed class ThumbnailPoller : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    // Not much shorter than this: Windows retries a refused connection for
    // ~2 s before reporting it, and "refused" is what tells "nobody logged
    // in" apart from "offline".
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private const int ReplyTimeoutMs = 10_000;

    // After a failed attempt at one of the Agent chores below (an Agent not
    // running); an answer, even "don't know", isn't asked for again.
    public static readonly TimeSpan DefaultAgentRetryInterval = TimeSpan.FromMinutes(5);

    // After the Agent refused the class folder's lesson as busy with its
    // student's folders (ClassHomeClaim.BusyText): it took nothing and is
    // asked again in a moment, as the line the teacher saw says (Ruling 23).
    public static readonly TimeSpan DefaultBusyRetryInterval = TimeSpan.FromSeconds(10);

    // How often a machine is asked what program block it follows, for its tile.
    private static readonly TimeSpan AppBlockObserveInterval = TimeSpan.FromSeconds(30);

    private readonly KnownMachine _machine;
    private readonly X509Certificate2 _masterCertificate;
    private readonly int _port;
    private readonly int _agentPort;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly Func<PowerSchedule?>? _powerSchedule;
    private readonly Func<AppBlockPolicy?>? _appBlockPolicy;
    private readonly Func<(int Width, int Height)>? _thumbnailSize;
    private readonly Func<ClassLesson?>? _classLesson;
    private readonly Func<ClassHomeClaim?>? _classHomeClaim;
    private readonly Func<ClassLesson, ClassHome?, ClassLesson?>? _classLessonOver;
    private readonly Func<bool>? _classLessonHeldEnd;
    private readonly TimeSpan _agentRetryInterval;
    private long? _sentAppBlockSavedAt;
    private DateTime _nextAppBlockAttempt = DateTime.MinValue;
    private DateTime _nextAppBlockObservation = DateTime.MinValue;
    private bool _macAddressAnswered;
    private DateTime _nextMacAddressAttempt = DateTime.MinValue;
    private long? _sentScheduleSavedAt;
    private DateTime _nextScheduleAttempt = DateTime.MinValue;
    private long? _sentClassLessonSavedAt;
    private long? _failedClassLessonSavedAt;
    private DateTime _nextClassLessonAttempt = DateTime.MinValue;
    private long? _checkedClassHomeGeneration;
    private DateTime _nextClassHomeAttempt = DateTime.MinValue;

    // powerSchedule: the teacher's current timetable (read on the poller's
    // thread), which this machine's Agent is given whenever it changes.
    // thumbnailSize: how big the grid's tiles are now (TileSize.Request),
    // read before each request; without it, the size tiles always had.
    // classLesson: the class folder's lesson (ClassFolderRole.LessonForPollers),
    // handed to the Agent like the program block. classHomeClaim: while this
    // PC keeps the class folders, its claim on an Agent that keeps an older
    // home (ClassFolderRole.HomeGeneration); null otherwise. classLessonOver:
    // for a lesson the Agent follows and the home it keeps, that lesson's end
    // when this PC knows it is over (ClassFolderRole.EndOfLessonOver); null otherwise. classLessonHeldEnd:
    // whether classLesson is the end of a lesson this PC held for the room
    // while another PC is the home (ClassFolderRole.HandsOutAHeldEnd), handed
    // only to an Agent still in that lesson.
    public ThumbnailPoller(
        KnownMachine machine, X509Certificate2 masterCertificate,
        int port = ConnectionClient.SessionPort, int agentPort = ConnectionClient.AgentPort,
        Func<PowerSchedule?>? powerSchedule = null, Func<AppBlockPolicy?>? appBlockPolicy = null,
        TimeSpan? agentRetryInterval = null, Func<(int Width, int Height)>? thumbnailSize = null,
        Func<ClassLesson?>? classLesson = null, Func<ClassHomeClaim?>? classHomeClaim = null,
        Func<ClassLesson, ClassHome?, ClassLesson?>? classLessonOver = null, Func<bool>? classLessonHeldEnd = null)
    {
        _machine = machine;
        _masterCertificate = masterCertificate;
        _port = port;
        _agentPort = agentPort;
        _powerSchedule = powerSchedule;
        _appBlockPolicy = appBlockPolicy;
        _thumbnailSize = thumbnailSize;
        _classLesson = classLesson;
        _classHomeClaim = classHomeClaim;
        _classLessonOver = classLessonOver;
        _classLessonHeldEnd = classLessonHeldEnd;
        _agentRetryInterval = agentRetryInterval ?? DefaultAgentRetryInterval;
        _thread = new Thread(Run) { IsBackground = true, Name = $"Thumbnails: {machine.Name}" };
    }

    // All raised on the poller's own thread. An empty thumbnail means the
    // machine answered but its screen can't be captured right now (e.g. it's
    // locked); the string is a short reason to show on the tile.
    public event Action<byte[]>? ThumbnailReceived;
    public event Action<string>? Unreachable;

    // The MAC address ("AA-BB-CC-DD-EE-FF") of the machine's card on this
    // network, as its Agent reports it: asked once per poller, the first
    // time the machine answers -- i.e. once each time Master starts, which
    // also catches a card that was replaced.
    public event Action<string>? MacAddressLearned;

    // What program block the machine's Agent says it follows, for the tile's
    // badge: every so often while the machine is on. Null when the Agent
    // didn't answer (not running).
    public event Action<AppBlockClient.StatusResult?>? AppBlockObserved;

    // DefaultBusyRetryInterval, or a test's.
    internal TimeSpan BusyRetryInterval { get; init; } = DefaultBusyRetryInterval;

    public void Start() => _thread.Start();

    // The thread notices within one poll interval or network timeout; it's a
    // background thread, so it never holds up Master from exiting.
    public void Dispose() => _stop.Cancel();

    private void Run()
    {
        TcpClient? tcpClient = null;
        SecureChannel? channel = null;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (channel is null)
                {
                    tcpClient = ConnectTcp();
                    channel = SecureChannel.AuthenticateAsClient(
                        tcpClient, _machine.HostAddress, _masterCertificate,
                        new TrustStore(new[] { _machine.SessionThumbprint }));
                }

                var request = _thumbnailSize?.Invoke() is { } size
                    ? ThumbnailRequest.Encode(size.Width, size.Height)
                    : Array.Empty<byte>();
                channel.Send(new Message(MessageType.RequestThumbnail, request));
                var reply = channel.Receive();
                if (reply.Type != MessageType.ThumbnailFrame)
                {
                    throw new InvalidDataException($"Expected a thumbnail, got {reply.Type}");
                }

                if (!_stop.IsCancellationRequested)
                {
                    ThumbnailReceived?.Invoke(reply.Payload);
                }

                AskForMacAddressOnce();
                SendPowerScheduleIfChanged();
                SendAppBlockIfChanged();
                // The home first: an Agent that keeps an older one refuses its lesson.
                ClaimClassHomeIfBehind();
                SendClassLessonIfChanged();
                ObserveAppBlock();
            }
            catch (Exception ex)
            {
                // Always start over with a fresh connection: a reply that
                // timed out could still arrive later and would then be read
                // as the answer to the next request.
                channel?.Dispose();
                tcpClient?.Dispose();
                channel = null;
                tcpClient = null;
                // Its Agent's home is looked at again once it answers again.
                _checkedClassHomeGeneration = null;

                if (!_stop.IsCancellationRequested)
                {
                    Unreachable?.Invoke(FailureText.Status(ex));
                }
            }

            _stop.Token.WaitHandle.WaitOne(PollInterval);
        }

        channel?.Dispose();
        tcpClient?.Dispose();
    }

    // Only right after a thumbnail, when the machine is known to be on, so
    // an offline machine never costs a connection timeout here.
    private void AskForMacAddressOnce()
    {
        if (_macAddressAnswered || _machine.AgentThumbprint is not { Length: > 0 } agentThumbprint
            || DateTime.UtcNow < _nextMacAddressAttempt || _stop.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var reply = CommandClient.Ask(
                _machine.HostAddress, _agentPort, agentThumbprint, _masterCertificate,
                MessageType.GetWakeAddress, Array.Empty<byte>());
            _macAddressAnswered = true;
            if (reply.Type == MessageType.WakeAddress && WakeOnLan.IsUsableMacAddress(reply.Payload)
                && !_stop.IsCancellationRequested)
            {
                MacAddressLearned?.Invoke(WakeOnLan.FormatMacAddress(reply.Payload));
            }
        }
        catch (Exception)
        {
            _nextMacAddressAttempt = DateTime.UtcNow + _agentRetryInterval;
        }
    }

    // Once per poller and again after each change, so a machine that was
    // off when the teacher saved the schedule gets it the next time it's on.
    // An older schedule than the one the Agent has is kept out by the Agent.
    private void SendPowerScheduleIfChanged()
    {
        if (_powerSchedule?.Invoke() is not { } schedule || schedule.SavedAt == _sentScheduleSavedAt
            || _machine.AgentThumbprint is not { Length: > 0 } agentThumbprint
            || DateTime.UtcNow < _nextScheduleAttempt || _stop.IsCancellationRequested)
        {
            return;
        }

        try
        {
            CommandClient.SendCommandForResult(
                _machine.HostAddress, _agentPort, agentThumbprint, _masterCertificate,
                MessageType.SetPowerSchedule, schedule.Encode());
            _sentScheduleSavedAt = schedule.SavedAt;
        }
        catch (Exception)
        {
            _nextScheduleAttempt = DateTime.UtcNow + _agentRetryInterval;
        }
    }

    // Once per poller and again after each change, so a machine that was
    // off when the teacher set the block gets it the next time it's on. The
    // Agent keeps an older policy out by SavedAt. Only an Agent that took it
    // counts as delivered: an older Agent (empty answer) or one that couldn't
    // save it is tried again after a while.
    private void SendAppBlockIfChanged()
    {
        if (_appBlockPolicy?.Invoke() is not { } policy || policy.SavedAt == _sentAppBlockSavedAt
            || _machine.AgentThumbprint is not { Length: > 0 } agentThumbprint
            || DateTime.UtcNow < _nextAppBlockAttempt || _stop.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var (succeeded, _) = CommandClient.SendCommandForResult(
                _machine.HostAddress, _agentPort, agentThumbprint, _masterCertificate,
                MessageType.SetAppBlock, policy.Encode());
            if (succeeded)
            {
                _sentAppBlockSavedAt = policy.SavedAt;
                _nextAppBlockObservation = DateTime.MinValue;
            }
            else
            {
                _nextAppBlockAttempt = DateTime.UtcNow + _agentRetryInterval;
            }
        }
        catch (Exception)
        {
            _nextAppBlockAttempt = DateTime.UtcNow + _agentRetryInterval;
        }
    }

    // The same for the class folder's lesson: a machine that was off when the
    // teacher started or ended it gets it the next time it's on, and the Agent
    // keeps an older one out by SavedAt; one that was busy (it waits for its
    // lesson lock no longer than ClassHomeClaim.MaxLockWait) gets it after
    // BusyRetryInterval, not the usual retry interval. The wait is that
    // lesson's: one started or ended since goes at once. A lesson never saved
    // (SavedAt 0: the Master hasn't had one yet) is nothing to hand over, nor is
    // no lesson at all (a Master that stood down, however saved), which an
    // Agent would take for a newer end of the lesson another teacher PC runs.
    // An Agent that keeps its own newer lesson instead is asked which: one
    // this PC knows is over (the PC was off for its end, and missed this
    // lesson's start) is ended there, and this lesson handed again, as an
    // Agent takes only the end of the lesson it follows. A held lesson's end
    // (classLessonHeldEnd) goes only to an Agent still in that lesson: one
    // with no lesson on would take it, and then hold an end newer than the
    // other home's lesson, which it would refuse.
    private void SendClassLessonIfChanged()
    {
        if (_classLesson?.Invoke() is not { SavedAt: > 0 } lesson || lesson.LessonId == Guid.Empty
            || lesson.SavedAt == _sentClassLessonSavedAt
            || _machine.AgentThumbprint is not { Length: > 0 } agentThumbprint
            || (lesson.SavedAt == _failedClassLessonSavedAt && DateTime.UtcNow < _nextClassLessonAttempt)
            || _stop.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (!lesson.IsOn && _classLessonHeldEnd?.Invoke() == true && !StillIn(agentThumbprint, lesson))
            {
                _sentClassLessonSavedAt = lesson.SavedAt;
                return;
            }
            var (succeeded, detail) = SendClassLesson(agentThumbprint, lesson);
            if (succeeded && detail.StartsWith(ClassLesson.KeptNewerText, StringComparison.Ordinal) && EndLessonOver(agentThumbprint))
            {
                (succeeded, detail) = SendClassLesson(agentThumbprint, lesson);
            }
            if (succeeded)
            {
                _sentClassLessonSavedAt = lesson.SavedAt;
            }
            else
            {
                // Busy with its student's folders, it is asked again in a moment (an end
                // that waited minutes would leave that PC in the lesson meanwhile).
                ClassLessonFailed(lesson, detail == ClassHomeClaim.BusyText ? BusyRetryInterval : _agentRetryInterval);
            }
        }
        catch (Exception)
        {
            ClassLessonFailed(lesson, _agentRetryInterval);
        }
    }

    private void ClassLessonFailed(ClassLesson lesson, TimeSpan wait)
    {
        _failedClassLessonSavedAt = lesson.SavedAt;
        _nextClassLessonAttempt = DateTime.UtcNow + wait;
    }

    private (bool Succeeded, string Detail) SendClassLesson(string agentThumbprint, ClassLesson lesson) =>
        CommandClient.SendCommandForResult(
            _machine.HostAddress, _agentPort, agentThumbprint, _masterCertificate, MessageType.SetClassLesson, lesson.Encode());

    // Whether the Agent still follows that lesson, on; one that follows a
    // lesson this PC knows is over has it ended there (classLessonOver).
    private bool StillIn(string agentThumbprint, ClassLesson lesson)
    {
        var (followed, _) = ClassFolderClient.GetLesson(_machine, _masterCertificate, _agentPort);
        if (followed is { IsOn: true } && followed.LessonId == lesson.LessonId)
        {
            return true;
        }
        if (followed is not null)
        {
            EndLessonOver(agentThumbprint, followed);
        }
        return false;
    }

    // The lesson the Agent follows (followed, or read now), ended there when
    // this PC knows it is over (classLessonOver), which goes by the home the
    // Agent keeps (keptHome when read already, or read now): true when the
    // Agent took that end.
    private bool EndLessonOver(string agentThumbprint, ClassLesson? followed = null, (ClassHome? Home, bool Read) keptHome = default)
    {
        if (_classLessonOver is null || _stop.IsCancellationRequested)
        {
            return false;
        }
        followed ??= ClassFolderClient.GetLesson(_machine, _masterCertificate, _agentPort).Lesson;
        if (followed is null)
        {
            return false;
        }
        var home = keptHome.Read ? keptHome.Home : ClassFolderClient.GetHome(_machine, _masterCertificate, _agentPort).Home;
        if (_classLessonOver(followed, home) is not { } end)
        {
            return false;
        }
        var (succeeded, detail) = SendClassLesson(agentThumbprint, end);
        return succeeded && !detail.StartsWith(ClassLesson.KeptNewerText, StringComparison.Ordinal);
    }

    // The class folders' home (Ruling 9): while this PC keeps them, an Agent
    // that keeps an older home, another PC's or this PC's own at an older
    // generation (a straggler: off when this PC took the folders over), is
    // claimed with this PC's (classHomeClaim) once its machine answers, each
    // time it comes back. The Agent itself refuses it while it follows a lesson
    // of the home it keeps (tried again after the retry interval, or in a
    // moment when it was busy), and takes this PC's own as a refresh. One that
    // keeps no home, or one at least as new, is left as it is. A lesson the
    // straggler still follows that this PC knows is over (it came with the
    // old home's folders: the PC was off for its end too) is ended there
    // first, or nobody would ever end it, and the PC would stay out of this
    // PC's lessons until the day ends.
    private void ClaimClassHomeIfBehind()
    {
        if (_classHomeClaim?.Invoke() is not { } claim || claim.Generation == _checkedClassHomeGeneration
            || _machine.AgentThumbprint is not { Length: > 0 } agentThumbprint
            || DateTime.UtcNow < _nextClassHomeAttempt || _stop.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var (home, oldAgent) = ClassFolderClient.GetHome(_machine, _masterCertificate, _agentPort);
            if (!oldAgent && home is not null && home.Generation < claim.Generation)
            {
                EndLessonOver(agentThumbprint, keptHome: (home, true));
                var (ok, detail, _) = ClassFolderClient.ClaimHome(_machine, _masterCertificate, claim, _agentPort);
                if (!ok)
                {
                    _nextClassHomeAttempt = DateTime.UtcNow
                        + (detail == ClassHomeClaim.BusyText ? BusyRetryInterval : _agentRetryInterval);
                    return;
                }
            }
            _checkedClassHomeGeneration = claim.Generation;
        }
        catch (Exception)
        {
            _nextClassHomeAttempt = DateTime.UtcNow + _agentRetryInterval;
        }
    }

    private void ObserveAppBlock()
    {
        if (_machine.AgentThumbprint is not { Length: > 0 } || DateTime.UtcNow < _nextAppBlockObservation
            || _stop.IsCancellationRequested)
        {
            return;
        }

        AppBlockClient.StatusResult? status = null;
        try
        {
            status = AppBlockClient.GetStatus(_machine, _masterCertificate, _agentPort);
        }
        catch (Exception)
        {
            // No Agent answering: no badge.
        }

        _nextAppBlockObservation = DateTime.UtcNow + AppBlockObserveInterval;
        if (!_stop.IsCancellationRequested)
        {
            AppBlockObserved?.Invoke(status);
        }
    }

    private TcpClient ConnectTcp()
    {
        var tcpClient = TcpConnect.WithTimeout(_machine.HostAddress, _port, ConnectTimeout, _stop.Token);
        tcpClient.NoDelay = true;
        tcpClient.ReceiveTimeout = ReplyTimeoutMs;
        tcpClient.SendTimeout = ReplyTimeoutMs;
        return tcpClient;
    }
}
