using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// The class folders' home PC serving its "Pasta da turma" window to a
// colleague's teacher PC ("Two teacher PCs in one lab",
// docs/superpowers/specs/2026-10-20-class-folder-design.md; plan decisions 5
// and 6): it listens on ClassFolderRemote.Port over TLS, takes one request per
// connection and answers it from the home's own controller (local), so a
// colleague's command runs one at a time with the home teacher's own clicks.
//
// Any teacher certificate gets through the handshake; then only a colleague is
// served: a PC with this PC's own identity (two teacher PCs can share one), or
// one isTrusted says the room's Agents trust (ColleagueTrust: a colleague given
// access with "Dar acesso a um colega"). Anyone else, and any message that
// isn't RemoteClassFolderState or RemoteClassFolderCommand, gets one
// CommandAck with a CommandResult saying no (ClassFolderRemote's texts), after
// their request was read. What the messages can't carry is left out or cut
// (Ruling 4): a class folder made by hand with a name over
// ClassLesson.MaxTurmaLength is not listed, and a PC's name is cut to
// ClassHome.MaxNameLength. What a colleague's PC sends as a class is taken only
// when it is a class name (ClassStore.IsValidName); and what the home tells it
// is always in the teacher's words, never an exception's own message.
//
// The handshake and the request must be in within ReadTimeout, however they
// trickle in; a command may then run for CommandTimeout (a lesson's end brings
// the last files in first), after which the colleague is told it is still
// running. At most MaxConnections at once, MaxPerAddress from one PC. Nothing a
// client does (nonsense, hanging up, never finishing) stops the host: the next
// connection is served. In two files: this one (the connections) and
// ClassFolderHost.Answers.cs (what each request gets).
public sealed partial class ClassFolderHost : IDisposable
{
    // Requests are a few names at most; anything longer is refused before it is read.
    private const int MaxRequestBytes = 64 * 1024;

    // Colleagues' PCs ask every couple of seconds, one connection each: more
    // at once than this is someone flooding the port, and is closed unanswered.
    private const int MaxConnections = 8;

    // One PC: a state read, a command running, and the state read right after it.
    private const int MaxPerAddress = 3;

    // How long the answer to "is this a colleague?" may take at most (ColleagueTrust bounds its own well under it).
    private static readonly TimeSpan TrustWait = TimeSpan.FromSeconds(10);

    private readonly X509Certificate2 _certificate;
    private readonly IClassFolderController _local;
    private readonly Func<string, Task<bool>> _isTrusted;
    private readonly Action<string> _log;
    private readonly TcpListener _tcp;
    private readonly CancellationTokenSource _stop = new();

    // The connections being served, in all and by the address they came from. Under _connectionsLock.
    private readonly object _connectionsLock = new();
    private readonly Dictionary<IPAddress, int> _byAddress = new();
    private int _connections;

    private ClassFolderHost(
        X509Certificate2 certificate, IClassFolderController local, Func<string, Task<bool>> isTrusted, Action<string> log,
        TcpListener tcp)
    {
        _certificate = certificate;
        _local = local;
        _isTrusted = isTrusted;
        _log = log;
        _tcp = tcp;
        Port = ((IPEndPoint)tcp.LocalEndpoint).Port;
    }

    // The port it listens on (the one asked for, or the one the system gave for 0).
    public int Port { get; }

    // How long a client has for the handshake and its request together, and to take the answer.
    internal TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(5);

    // Starts listening on every address (address: one only, for the tests)
    // and serving on the thread pool, never on the caller's context (Master
    // calls this on its UI thread). isTrusted: ColleagueTrust.IsTrustedAsync.
    // Null when the port can't be had (another program holds it): logged, in
    // English, by default to System.Diagnostics.Trace.
    public static ClassFolderHost? Open(
        X509Certificate2 certificate, IClassFolderController local, Func<string, Task<bool>> isTrusted,
        int port = ClassFolderRemote.Port, Action<string>? log = null, IPAddress? address = null)
    {
        log ??= message => System.Diagnostics.Trace.WriteLine("ClassFolderHost: " + message);
        var tcp = new TcpListener(address ?? IPAddress.Any, port);
        try
        {
            tcp.ExclusiveAddressUse = true;
            tcp.Start();
        }
        catch (SocketException ex)
        {
            tcp.Stop();
            log($"Could not listen on port {port} for colleagues' teacher PCs: {ex.Message}");
            return null;
        }

        var host = new ClassFolderHost(certificate, local, isTrusted, log, tcp);
        _ = Task.Run(host.AcceptAsync);
        return host;
    }

    // Stops listening. A request already being served is answered, and a
    // command already running on the home goes on.
    public void Dispose()
    {
        _stop.Cancel();
        _tcp.Stop();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _tcp.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }
                // A connection reset before it was accepted, say: the next one is still taken.
                _log($"Accepting a connection failed: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                continue;
            }

            if (AddressOf(client) is not { } address || !TryTake(address))
            {
                client.Dispose();
                continue;
            }

            // Its own thread, as the Agent does: the exchange blocks on the
            // network, and a command can wait minutes for the PCs.
            _ = Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        Handle(client);
                    }
                    finally
                    {
                        client.Dispose();
                        Release(address);
                    }
                },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    // Null for a client already gone.
    private static IPAddress? AddressOf(TcpClient client)
    {
        try
        {
            return (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            return null;
        }
    }

    private bool TryTake(IPAddress address)
    {
        lock (_connectionsLock)
        {
            var fromThere = _byAddress.GetValueOrDefault(address);
            if (_connections >= MaxConnections || fromThere >= MaxPerAddress)
            {
                return false;
            }
            _connections++;
            _byAddress[address] = fromThere + 1;
            return true;
        }
    }

    private void Release(IPAddress address)
    {
        lock (_connectionsLock)
        {
            _connections--;
            var left = _byAddress[address] - 1;
            if (left == 0)
            {
                _byAddress.Remove(address);
            }
            else
            {
                _byAddress[address] = left;
            }
        }
    }

    private void Handle(TcpClient client)
    {
        var timeout = (int)ReadTimeout.TotalMilliseconds;
        client.ReceiveTimeout = timeout;
        client.SendTimeout = timeout;
        SecureChannel? channel = null;
        try
        {
            Message request;
            // The handshake and the request, whole, by the deadline: a client
            // trickling bytes never waits long on one read, so the deadline
            // closes it. Read whole before answering: closing with a request
            // unread would make Windows reset the connection, and lose the answer.
            using (var deadline = new CancellationTokenSource(ReadTimeout))
            using (deadline.Token.Register(client.Dispose))
            {
                channel = SecureChannel.AuthenticateAsServerAcceptingAnyClient(client, _certificate);
                request = channel.Receive(MaxRequestBytes);
            }
            channel.Send(IsColleague(channel.RemoteThumbprint) ? Answer(request) : Refusal(ClassFolderRemote.NotTrustedText));
        }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or InvalidDataException
            or ObjectDisposedException)
        {
            // A client that left, sent nonsense or too much, or missed the deadline.
        }
        catch (Exception ex)
        {
            _log($"Serving a colleague's teacher PC failed: {ex}");
        }
        finally
        {
            channel?.Dispose();
        }
    }

    private bool IsColleague(string thumbprint)
    {
        if (thumbprint.Length == 0)
        {
            return false;
        }
        if (string.Equals(thumbprint, _certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        try
        {
            var trusted = _isTrusted(thumbprint);
            return trusted.Wait(TrustWait) && trusted.Result;
        }
        catch (Exception ex)
        {
            _log($"Asking whether {thumbprint} is a colleague failed: {ex.Message}");
            return false;
        }
    }

    private static Message Refusal(string detail) => new(MessageType.CommandAck, CommandResult.Encode(false, detail));
}
