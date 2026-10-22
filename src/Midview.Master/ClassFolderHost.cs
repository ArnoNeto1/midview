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
// one isTrusted says the room's Agents trust (a colleague given access with
// "Dar acesso a um colega"). Anyone else, and any message that isn't
// RemoteClassFolderState or RemoteClassFolderCommand, gets one CommandAck with
// a CommandResult saying no, after their request was read. What the messages
// can't carry is left out or cut (Ruling 4): a class folder made by hand with a
// name over ClassLesson.MaxTurmaLength is not listed, and a PC's name is cut to
// ClassHome.MaxNameLength.
//
// The request is read within ReadTimeout; a command may run for CommandTimeout
// (a lesson's end brings the last files in first), after which the colleague
// is told it is still running. Nothing a client does (nonsense, hanging up,
// never finishing the handshake) stops the host: the next connection is served.
public sealed class ClassFolderHost : IDisposable
{
    public const string NotTrustedText =
        "O PC que guarda as pastas das turmas recusou o pedido: este computador não tem acesso aos PCs desta sala.";

    public const string UnknownRequestText =
        "O PC que guarda as pastas das turmas não entendeu o pedido. Atualize o Midview nos dois PCs de professor.";

    public const string IncompleteText = "O pedido chegou incompleto ao PC que guarda as pastas das turmas. Tente de novo.";

    public const string StillRunningText =
        "O PC que guarda as pastas das turmas ainda está fazendo isso. Veja o resultado na janela em instantes.";

    public const string UnreadableText = "O PC que guarda as pastas das turmas não conseguiu lê-las agora: ";

    // Requests are a few names at most; anything longer is refused before it is read.
    private const int MaxRequestBytes = 64 * 1024;

    // Colleagues' PCs ask every couple of seconds, one connection each: more
    // at once than this is someone flooding the port, and is closed unanswered.
    private const int MaxConnections = 8;

    // How long the answer to "is this a colleague?" may take (the Agents are asked).
    private static readonly TimeSpan TrustWait = TimeSpan.FromSeconds(30);

    private readonly X509Certificate2 _certificate;
    private readonly IClassFolderController _local;
    private readonly Func<string, Task<bool>> _isTrusted;
    private readonly Action<string> _log;
    private readonly TcpListener _tcp;
    private readonly CancellationTokenSource _stop = new();
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

    // How long a client has to send its request, and to take the answer.
    internal TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(5);

    // Starts listening on every address (address: one only, for the tests)
    // and serving on the thread pool, never on the caller's context (Master
    // calls this on its UI thread). Null when the port can't be had (another
    // program holds it): logged, in English, by default to System.Diagnostics.Trace.
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

    // Stops listening. A command already running on the home goes on.
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

            if (Interlocked.Increment(ref _connections) > MaxConnections)
            {
                Interlocked.Decrement(ref _connections);
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
                        Interlocked.Decrement(ref _connections);
                    }
                },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    private void Handle(TcpClient client)
    {
        var timeout = (int)ReadTimeout.TotalMilliseconds;
        client.ReceiveTimeout = timeout;
        client.SendTimeout = timeout;
        try
        {
            using var channel = SecureChannel.AuthenticateAsServerAcceptingAnyClient(client, _certificate);
            // Read whole before answering: closing with a request unread would
            // make Windows reset the connection, and the answer could be lost.
            var request = channel.Receive(MaxRequestBytes);
            channel.Send(IsColleague(channel.RemoteThumbprint) ? Answer(request) : Refusal(NotTrustedText));
        }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or InvalidDataException
            or ObjectDisposedException)
        {
            // A client that left, sent nonsense or never finished the handshake.
        }
        catch (Exception ex)
        {
            _log($"Serving a colleague's teacher PC failed: {ex}");
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

    private Message Answer(Message request)
    {
        switch (request.Type)
        {
            case MessageType.RemoteClassFolderState:
                RemoteState state;
                try
                {
                    // The view the home itself shows: the lesson's numbers while one is on, the class list otherwise.
                    var read = _local.ReadAsync(lessonFiles: _local.Lesson.IsOn);
                    if (!read.Wait(CommandTimeout))
                    {
                        return Refusal(UnreadableText + "demorou demais.");
                    }
                    state = read.Result;
                }
                catch (Exception ex)
                {
                    return Refusal(UnreadableText + ErrorText.Describe(ex is AggregateException { InnerException: { } inner } ? inner : ex));
                }
                return new Message(MessageType.RemoteClassFolderStateReply, ClassFolderRemote.EncodeJson(ForTheWire(state)));

            case MessageType.RemoteClassFolderCommand:
                var command = ClassFolderRemote.DecodeJson<RemoteCommand>(request.Payload);
                RemoteReply reply;
                if (command is null)
                {
                    reply = new RemoteReply(false, IncompleteText, Array.Empty<ResultLineDto>(), null);
                }
                else
                {
                    var running = RunAsync(command);
                    reply = running.Wait(CommandTimeout)
                        ? running.Result
                        : new RemoteReply(false, StillRunningText, Array.Empty<ResultLineDto>(), command.Turma);
                }
                return new Message(MessageType.RemoteClassFolderCommandReply, ClassFolderRemote.EncodeJson(ForTheWire(reply)));

            default:
                return Refusal(UnknownRequestText);
        }
    }

    // The command, as if clicked in the home's window; never throws. A class
    // created is named by Turma (the name asked for); the reply's Turma is the
    // class it ended up about (the folder's name a create or a rename gave it).
    private async Task<RemoteReply> RunAsync(RemoteCommand command)
    {
        var none = Array.Empty<ResultLineDto>();
        var turma = command.Turma;
        try
        {
            switch (command.Kind)
            {
                case RemoteCommand.Create when turma is not null:
                    var created = await _local.CreateAsync(turma).ConfigureAwait(false);
                    return new RemoteReply(created.Ok, created.Detail, none, created.Ok ? created.Turma : null);

                case RemoteCommand.Rename when turma is not null && command.NewName is { } newName:
                    var renamed = await _local.RenameAsync(turma, newName).ConfigureAwait(false);
                    return new RemoteReply(renamed.Ok, renamed.Detail, none, renamed.Ok ? ClassStore.CleanName(newName) : turma);

                case RemoteCommand.Delete when turma is not null:
                    var deleted = await _local.DeleteAsync(turma).ConfigureAwait(false);
                    return new RemoteReply(deleted.Ok, deleted.Detail, none, turma);

                case RemoteCommand.Start when turma is not null:
                    var started = await _local.StartAsync(turma).ConfigureAwait(false);
                    // Another teacher PC keeps the class folders: no lesson, and the lines say which.
                    var on = _local.Lesson is { IsOn: true } lesson && string.Equals(lesson.Turma, turma, StringComparison.OrdinalIgnoreCase);
                    return new RemoteReply(on, on ? string.Empty : ClassFolderText.NotStarted(turma), started, turma);

                case RemoteCommand.End when turma is not null && ChoiceOf(command.Choice) is { } choice:
                    var ended = await _local.EndAsync(turma, choice).ConfigureAwait(false);
                    return new RemoteReply(true, string.Empty, ended, turma);

                default:
                    return new RemoteReply(false, IncompleteText, none, turma);
            }
        }
        catch (ClassFolderRefusedException ex)
        {
            return new RemoteReply(false, ex.Message, none, turma);
        }
        catch (Exception ex)
        {
            // What the home's own window says when that fails.
            var reason = ErrorText.Describe(ex);
            var detail = command.Kind switch
            {
                RemoteCommand.Start => ClassFolderText.StartFailed(turma ?? string.Empty, reason),
                RemoteCommand.End => ClassFolderText.EndFailed(turma ?? string.Empty, reason),
                RemoteCommand.Delete => ClassFolderText.DeleteFailed(reason),
                _ => reason,
            };
            _log($"A colleague's {command.Kind} failed: {ex}");
            return new RemoteReply(false, detail, none, turma);
        }
    }

    private static EndChoice? ChoiceOf(string? choice) => choice switch
    {
        RemoteCommand.Keep => EndChoice.Keep,
        RemoteCommand.Today => EndChoice.DeleteToday,
        RemoteCommand.All => EndChoice.DeleteAll,
        _ => null,
    };

    private static RemoteState ForTheWire(RemoteState state) => state with
    {
        HomeName = Cut(state.HomeName),
        Classes = state.Classes
            .Where(summary => summary.Turma.Length <= ClassLesson.MaxTurmaLength && summary.Files >= 0 && summary.Bytes >= 0)
            .ToList(),
        Pcs = state.Pcs.Select(pc => pc with { Name = Cut(pc.Name) }).ToList(),
        FreeBytes = Math.Max(0, state.FreeBytes),
        RecycleBinBytes = Math.Max(0, state.RecycleBinBytes),
    };

    private static RemoteReply ForTheWire(RemoteReply reply) =>
        reply with { Lines = reply.Lines.Select(line => line with { Name = Cut(line.Name) }).ToList() };

    // At most ClassHome.MaxNameLength characters, never half of a surrogate pair.
    private static string Cut(string name)
    {
        if (name.Length <= ClassHome.MaxNameLength)
        {
            return name;
        }
        var length = char.IsHighSurrogate(name[ClassHome.MaxNameLength - 1]) ? ClassHome.MaxNameLength - 1 : ClassHome.MaxNameLength;
        return name[..length];
    }

    private static Message Refusal(string detail) => new(MessageType.CommandAck, CommandResult.Encode(false, detail));
}
