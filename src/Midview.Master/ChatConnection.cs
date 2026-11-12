using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// The teacher's side of a private chat with one machine: one connection for
// the whole conversation. Opening it pops the chat window up on the
// student's screen; closing it (Dispose) ends the conversation there.
public sealed class ChatConnection : IChatConnection
{
    // Well inside the Session's 15 s receive timeout, so a conversation where
    // nobody types for a while isn't dropped as a dead connection.
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    private readonly TcpClient _tcpClient;
    private readonly SecureChannel _channel;
    private readonly object _sendLock = new();
    private readonly Thread _receiveThread;
    private readonly Timer _heartbeatTimer;
    private int _closed;

    private ChatConnection(TcpClient tcpClient, SecureChannel channel)
    {
        _tcpClient = tcpClient;
        _channel = channel;
        _receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "Chat receive" };
        _heartbeatTimer = new Timer(_ => TrySend(new Message(MessageType.Heartbeat, Array.Empty<byte>())));
    }

    // Raised on whichever thread finds out: StudentMessageReceived on a
    // background thread; Closed there too when the student's side ends it,
    // or on the caller's thread from Dispose or a Send that fails. Closed is
    // raised once, however the conversation ended, with a short reason.
    public event Action<string>? StudentMessageReceived;
    public event Action<string>? Closed;

    // A phone's agent says yes or no to the conversation first (see Open):
    // what it said when that is worth showing the teacher (it comes up once
    // the teacher's lock screen goes); null otherwise.
    public string? Notice { get; private init; }

    // An agent from before "Bate-papo" refuses it in these words.
    public static readonly string OldAgent = PhoneAgentCommand.OldAgent("conversa");

    // What the phone says when its screen came up.
    private const string PhoneOpened = "conversa aberta";

    // A PC: its Session (port 5901), which opens the window and answers
    // nothing. A phone: its agent (port 28900, its own thumbprint), which
    // answers ChatOpen with one CommandAck first -- a refusal (an older
    // agent's "Ainda não funciona em celular." among them) throws
    // ChatRefusedException. port: only for the tests.
    public static ChatConnection Open(
        KnownMachine machine, X509Certificate2 masterCertificate, int? port = null,
        TimeSpan? handshakeTimeout = null)
    {
        var thumbprint = machine.IsPhone
            ? machine.AgentThumbprint is { Length: > 0 } agent ? agent : throw new ChatRefusedException(PhoneAgentCommand.NoIdentity)
            : machine.SessionThumbprint;
        var tcpClient = TcpConnect.WithTimeout(
            machine.HostAddress, port ?? (machine.IsPhone ? ConnectionClient.AgentPort : ConnectionClient.SessionPort), ConnectTimeout);
        try
        {
            tcpClient.NoDelay = true;
            // A frozen Session still completes the TCP handshake (Windows does
            // it from the listen backlog) but never answers TLS: give up
            // instead of waiting on "Conectando..." forever.
            var timeoutMs = (int)(handshakeTimeout ?? HandshakeTimeout).TotalMilliseconds;
            tcpClient.ReceiveTimeout = timeoutMs;
            tcpClient.SendTimeout = timeoutMs;
            var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, machine.HostAddress, masterCertificate, new TrustStore(new[] { thumbprint }));
            channel.Send(new Message(MessageType.ChatOpen, Array.Empty<byte>()));
            string? notice = null;
            if (machine.IsPhone)
            {
                try
                {
                    notice = PhoneAnswer(channel.Receive());
                }
                catch
                {
                    channel.Dispose();
                    throw;
                }
            }
            // From here on the receive loop waits for the student's lines for
            // as long as the conversation lasts: the Session sends the teacher
            // no heartbeats, so silence isn't a dead connection.
            tcpClient.ReceiveTimeout = 0;
            return new ChatConnection(tcpClient, channel) { Notice = notice };
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }

    // The phone agent's answer to ChatOpen: its notice (null for a plain
    // yes), or ChatRefusedException with why not.
    internal static string? PhoneAnswer(Message answer)
    {
        if (answer.Type != MessageType.CommandAck)
        {
            throw new ChatRefusedException($"resposta inesperada ({answer.Type})");
        }
        var (opened, detail) = CommandResult.Decode(answer.Payload);
        if (PhoneAgentCommand.IsOldAgentAnswer(opened, detail))
        {
            throw new ChatRefusedException(OldAgent);
        }
        if (!opened)
        {
            throw new ChatRefusedException(detail);
        }
        return detail.Length == 0 || detail == PhoneOpened ? null : detail;
    }

    // Why the conversation with the machine did not open, for the teacher.
    public static string DescribeFailure(KnownMachine machine, Exception ex) => ex switch
    {
        ChatRefusedException refused => refused.Message,
        _ when machine.IsPhone => FailureText.DescribePhoneAgent(ex),
        _ => FailureText.Describe(ex),
    };

    // Separate from Open so event handlers can be attached before anything
    // can be received. The timer is armed first: a connection the student's
    // side drops at once makes the receive thread close it, timer included,
    // and arming a disposed timer would throw.
    public void Start()
    {
        _heartbeatTimer.Change(HeartbeatInterval, HeartbeatInterval);
        _receiveThread.Start();
    }

    // False if the line couldn't be sent; the conversation is over then.
    // Throws EncoderFallbackException for text that isn't valid UTF-16
    // (pasted with half an emoji), leaving the conversation on.
    public bool Send(string text) => TrySend(new Message(MessageType.ChatText, TextMessage.Encode(text)));

    public void Dispose() => Close("conversa encerrada");

    // Sends come from the UI (the teacher's lines) and from the heartbeat
    // timer, so they're serialized; the receive thread only reads. Catches
    // everything: it runs inside a Timer callback, where an escaping
    // exception would take the whole Master down, and any failure to send
    // means the connection is unusable anyway.
    private bool TrySend(Message message)
    {
        try
        {
            lock (_sendLock)
            {
                _channel.Send(message);
            }
            return true;
        }
        catch (Exception)
        {
            Close("a conexão caiu");
            return false;
        }
    }

    private void ReceiveLoop()
    {
        try
        {
            while (true)
            {
                var message = _channel.Receive();
                if (message.Type == MessageType.ChatText && TextMessage.Decode(message.Payload) is { } text)
                {
                    StudentMessageReceived?.Invoke(text);
                }
            }
        }
        catch (Exception)
        {
            Close("o aluno saiu ou a conexão caiu");
        }
    }

    private void Close(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        _heartbeatTimer.Dispose();
        _channel.Dispose();
        _tcpClient.Dispose();
        Closed?.Invoke(reason);
    }
}

// A phone that answered the conversation with a no: the message is its reason, in the teacher's words.
public sealed class ChatRefusedException(string reason) : Exception(reason);
