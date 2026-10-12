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

    // Both raised on a background thread. Closed is raised once, however the
    // conversation ended (including Dispose), with a short reason.
    public event Action<string>? StudentMessageReceived;
    public event Action<string>? Closed;

    public static ChatConnection Open(
        KnownMachine machine, X509Certificate2 masterCertificate, int port = ConnectionClient.SessionPort)
    {
        var tcpClient = TcpConnect.WithTimeout(machine.HostAddress, port, ConnectTimeout);
        try
        {
            tcpClient.NoDelay = true;
            var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, machine.HostAddress, masterCertificate, new TrustStore(new[] { machine.SessionThumbprint }));
            channel.Send(new Message(MessageType.ChatOpen, Array.Empty<byte>()));
            return new ChatConnection(tcpClient, channel);
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }

    // Separate from Open so event handlers can be attached before anything
    // can be received.
    public void Start()
    {
        _receiveThread.Start();
        _heartbeatTimer.Change(HeartbeatInterval, HeartbeatInterval);
    }

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
