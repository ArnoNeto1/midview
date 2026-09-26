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
    private static readonly TimeSpan AgentRetryInterval = TimeSpan.FromMinutes(5);

    private readonly KnownMachine _machine;
    private readonly X509Certificate2 _masterCertificate;
    private readonly int _port;
    private readonly int _agentPort;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly Func<PowerSchedule?>? _powerSchedule;
    private bool _macAddressAnswered;
    private DateTime _nextMacAddressAttempt = DateTime.MinValue;
    private long? _sentScheduleSavedAt;
    private DateTime _nextScheduleAttempt = DateTime.MinValue;

    // powerSchedule: the teacher's current timetable (read on the poller's
    // thread), which this machine's Agent is given whenever it changes.
    public ThumbnailPoller(
        KnownMachine machine, X509Certificate2 masterCertificate,
        int port = ConnectionClient.SessionPort, int agentPort = ConnectionClient.AgentPort,
        Func<PowerSchedule?>? powerSchedule = null)
    {
        _machine = machine;
        _masterCertificate = masterCertificate;
        _port = port;
        _agentPort = agentPort;
        _powerSchedule = powerSchedule;
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

                channel.Send(new Message(MessageType.RequestThumbnail, Array.Empty<byte>()));
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
            _nextMacAddressAttempt = DateTime.UtcNow + AgentRetryInterval;
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
            _nextScheduleAttempt = DateTime.UtcNow + AgentRetryInterval;
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
