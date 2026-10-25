using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using Midview.Protocol;

namespace Midview.Master;

// RemoteClassFolderController: the teacher's commands, and each request on
// the wire to the home (its own connection, the home's certificate pinned).
public sealed partial class RemoteClassFolderController
{
    public Task<(bool Ok, string Detail, string Turma)> CreateAsync(string name) => OneAtATime(async () =>
    {
        if (HomeClosed)
        {
            return (false, ClosedText, string.Empty);
        }
        var reply = await CommandAsync(new RemoteCommand(RemoteCommand.Create, name, null, null)).ConfigureAwait(false);
        return (reply.Ok, reply.Detail, reply.Ok ? reply.Turma ?? ClassStore.CleanName(name) : string.Empty);
    });

    public Task<(bool Ok, string Detail)> RenameAsync(string turma, string newName) => OneAtATime(async () =>
    {
        if (HomeClosed)
        {
            return (false, ClosedText);
        }
        var reply = await CommandAsync(new RemoteCommand(RemoteCommand.Rename, turma, newName, null)).ConfigureAwait(false);
        return (reply.Ok, reply.Detail);
    });

    public Task<(bool Ok, string Detail)> DeleteAsync(string turma) => OneAtATime(async () =>
    {
        if (HomeClosed)
        {
            return (false, ClosedText);
        }
        var reply = await CommandAsync(new RemoteCommand(RemoteCommand.Delete, turma, null, null)).ConfigureAwait(false);
        return (reply.Ok, reply.Detail);
    });

    // A start another teacher PC keeps the class folders for comes back not
    // Ok, with each PC's refusal: those lines, and Lesson stays as it was.
    public Task<IReadOnlyList<ResultLineDto>> StartAsync(string turma) => OneAtATime(async () =>
    {
        if (HomeClosed)
        {
            throw new ClassFolderRefusedException(ClosedText);
        }
        var reply = await CommandAsync(new RemoteCommand(RemoteCommand.Start, turma, null, null)).ConfigureAwait(false);
        return reply.Ok || reply.Lines.Count > 0 ? reply.Lines : throw new ClassFolderRefusedException(reply.Detail);
    });

    // Whether the last EndAsync ended the lesson straight on the Agents (the
    // home closed: RemoteClassFolderController.Closed.cs) rather than through
    // the home, decided as it ran: what the window's headline goes by (Ruling 22).
    public bool LastEndOnTheAgents { get; private set; }

    public Task<IReadOnlyList<ResultLineDto>> EndAsync(string turma, EndChoice choice) => OneAtATime(async () =>
    {
        LastEndOnTheAgents = false;
        if (HomeClosed)
        {
            if (choice != EndChoice.Keep)
            {
                throw new ClassFolderRefusedException(ClosedText);
            }
            var lines = await EndOnTheAgentsAsync(turma).ConfigureAwait(false);
            LastEndOnTheAgents = true;
            return lines;
        }
        var reply = await CommandAsync(new RemoteCommand(RemoteCommand.End, turma, null, ChoiceOf(choice))).ConfigureAwait(false);
        return reply.Ok ? reply.Lines : throw new ClassFolderRefusedException(reply.Detail);
    });

    // The command at the home, then its state again. The home's reply, or one
    // in the teacher's words when there was none.
    private async Task<RemoteReply> CommandAsync(RemoteCommand command)
    {
        var none = Array.Empty<ResultLineDto>();
        if (CurrentHome() is not { } home)
        {
            return new RemoteReply(false, ClosedText, none, command.Turma);
        }

        var request = new Message(MessageType.RemoteClassFolderCommand, ClassFolderRemote.EncodeJson(command));
        var exchanged = await RequestAsync(home, request, CommandWait).ConfigureAwait(false);
        var reply = exchanged.Reply;
        RemoteReply answer;
        if (exchanged.Kind == Kind.Unreachable)
        {
            answer = new RemoteReply(false, ClosedText, none, command.Turma);
        }
        else if (exchanged.Kind == Kind.WrongIdentity)
        {
            answer = new RemoteReply(false, ClassFolderText.HomeOtherIdentity(Name), none, command.Turma);
        }
        else if (reply?.Type == MessageType.RemoteClassFolderCommandReply
            && ClassFolderRemote.DecodeJson<RemoteReply>(reply.Payload) is { } decoded)
        {
            // Its lines about its own disk said of it (ClassFolderText.Relayed).
            var name = Name;
            answer = decoded with
            {
                Lines = decoded.Lines.Select(line => line with { Detail = ClassFolderText.Relayed(line.Detail, name) }).ToList(),
            };
        }
        else if (reply?.Type == MessageType.CommandAck && CommandResult.Decode(reply.Payload).Detail is { Length: > 0 } refusal)
        {
            answer = new RemoteReply(false, refusal, none, command.Turma);
        }
        else
        {
            answer = new RemoteReply(false, ClassFolderText.HomeNoAnswer(Name), none, command.Turma);
        }

        await PollOnceAsync().ConfigureAwait(false);
        return answer;
    }

    // Disposed while waiting its turn (the window closed meanwhile): ObjectDisposedException.
    private async Task<T> OneAtATime<T>(Func<Task<T>> command)
    {
        try
        {
            await _commands.WaitAsync(_disposing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new ObjectDisposedException(nameof(RemoteClassFolderController));
        }
        try
        {
            return await command().ConfigureAwait(false);
        }
        finally
        {
            Release(_commands);
        }
    }

    private static string ChoiceOf(EndChoice choice) => choice switch
    {
        EndChoice.Keep => RemoteCommand.Keep,
        EndChoice.DeleteToday => RemoteCommand.Today,
        EndChoice.DeleteAll => RemoteCommand.All,
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };

    // The request, sent again while the home turns it away (never after a
    // handshake that hung: Hung). Each exchange on its own thread, not the
    // pool's: it blocks on the network, a command for minutes.
    private async Task<Exchanged> RequestAsync(ClassHome home, Message request, TimeSpan wait)
    {
        for (var attempt = 1; ; attempt++)
        {
            var exchanged = await OwnThread(() => Exchange(home, request, wait)).ConfigureAwait(false);
            if (exchanged.Kind != Kind.TurnedAway || attempt == Attempts)
            {
                return exchanged;
            }
            await Task.Delay(AttemptPause).ConfigureAwait(false);
        }
    }

    // One request on its own connection. Unreachable: nobody took the
    // connection. TurnedAway: it was closed, or the handshake failed, before
    // the request went, so it wasn't done; Hung: the same, but the handshake
    // got no answer for the whole wait (this PC's read timed out), which isn't
    // worth trying again at once. WrongIdentity: the PC there
    // holds another certificate than the home's, and is told nothing. Lost:
    // the request went and no answer came back (it may have been done). The
    // home's certificate is checked here, after the handshake (which proves the
    // other end holds that certificate's key), so a PC with another one is told
    // apart from a connection cut off; the request never goes to it.
    private Exchanged Exchange(ClassHome home, Message request, TimeSpan wait)
    {
        TcpClient tcp;
        try
        {
            tcp = TcpConnect.WithTimeout(home.Address, home.Port, ConnectTimeout);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return new Exchanged(Kind.Unreachable, null);
        }

        using (tcp)
        {
            SecureChannel channel;
            try
            {
                tcp.ReceiveTimeout = (int)StateWait.TotalMilliseconds;
                tcp.SendTimeout = (int)StateWait.TotalMilliseconds;
                channel = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, home.Address, _certificate);
            }
            catch (Exception ex) when (IsNetwork(ex))
            {
                return new Exchanged(TimedOut(ex) ? Kind.Hung : Kind.TurnedAway, null);
            }

            using (channel)
            {
                if (!string.Equals(channel.RemoteThumbprint, home.Thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    return new Exchanged(Kind.WrongIdentity, null);
                }
                try
                {
                    channel.Send(request);
                    tcp.ReceiveTimeout = (int)wait.TotalMilliseconds;
                    if (wait > StateWait)
                    {
                        CommandClient.EnableKeepAlive(tcp.Client);
                    }
                    return new Exchanged(Kind.Answered, channel.Receive(ClassFolderRemote.MaxJsonBytes + 1));
                }
                catch (Exception ex) when (IsNetwork(ex))
                {
                    return new Exchanged(Kind.Lost, null);
                }
            }
        }
    }

    // Blocking network work, off the thread pool.
    private static Task<T> OwnThread<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static bool IsNetwork(Exception ex) =>
        ex is IOException or SocketException or AuthenticationException or InvalidDataException or ObjectDisposedException;

    // This PC's read gave up waiting (the socket's timeout), whatever wraps it.
    private static bool TimedOut(Exception ex)
    {
        for (Exception? cause = ex; cause is not null; cause = cause.InnerException)
        {
            if (cause is SocketException { SocketErrorCode: SocketError.TimedOut })
            {
                return true;
            }
        }
        return false;
    }

    private enum Kind
    {
        Answered,
        Unreachable,
        TurnedAway,
        Hung,
        WrongIdentity,
        Lost,
    }

    private readonly record struct Exchanged(Kind Kind, Message? Reply);
}
