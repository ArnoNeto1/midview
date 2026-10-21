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
    private static readonly TimeSpan DefaultAgentRetryInterval = TimeSpan.FromMinutes(5);

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
    private readonly TimeSpan _agentRetryInterval;
    private long? _sentAppBlockSavedAt;
    private DateTime _nextAppBlockAttempt = DateTime.MinValue;
    private DateTime _nextAppBlockObservation = DateTime.MinValue;
    private bool _macAddressAnswered;
    private DateTime _nextMacAddressAttempt = DateTime.MinValue;
    private long? _sentScheduleSavedAt;
    private DateTime _nextScheduleAttempt = DateTime.MinValue;
    private long? _sentClassLessonSavedAt;
    private DateTime _nextClassLessonAttempt = DateTime.MinValue;

    // powerSchedule: the teacher's current timetable (read on the poller's
    // thread), which this machine's Agent is given whenever it changes.
    // thumbnailSize: how big the grid's tiles are now (TileSize.Request),
    // read before each request; without it, the size tiles always had.
    // classLesson: the class folder's lesson (ClassFolderSync.Lesson), handed
    // to the Agent like the program block.
    public ThumbnailPoller(
        KnownMachine machine, X509Certificate2 masterCertificate,
        int port = ConnectionClient.SessionPort, int agentPort = ConnectionClient.AgentPort,
        Func<PowerSchedule?>? powerSchedule = null, Func<AppBlockPolicy?>? appBlockPolicy = null,
        TimeSpan? agentRetryInterval = null, Func<(int Width, int Height)>? thumbnailSize = null,
        Func<ClassLesson?>? classLesson = null)
    {
        _machine = machine;
        _masterCertificate = masterCertificate;
        _port = port;
        _agentPort = agentPort;
        _powerSchedule = powerSchedule;
        _appBlockPolicy = appBlockPolicy;
        _thumbnailSize = thumbnailSize;
        _classLesson = classLesson;
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
    // keeps an older one out by SavedAt. A lesson never saved (SavedAt 0: the
    // Master hasn't had one yet) is nothing to hand over.
    private void SendClassLessonIfChanged()
    {
        if (_classLesson?.Invoke() is not { SavedAt: > 0 } lesson || lesson.SavedAt == _sentClassLessonSavedAt
            || _machine.AgentThumbprint is not { Length: > 0 } agentThumbprint
            || DateTime.UtcNow < _nextClassLessonAttempt || _stop.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var (succeeded, _) = CommandClient.SendCommandForResult(
                _machine.HostAddress, _agentPort, agentThumbprint, _masterCertificate,
                MessageType.SetClassLesson, lesson.Encode());
            if (succeeded)
            {
                _sentClassLessonSavedAt = lesson.SavedAt;
            }
            else
            {
                _nextClassLessonAttempt = DateTime.UtcNow + _agentRetryInterval;
            }
        }
        catch (Exception)
        {
            _nextClassLessonAttempt = DateTime.UtcNow + _agentRetryInterval;
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
