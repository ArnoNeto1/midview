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

    public Task<IReadOnlyList<ResultLineDto>> EndAsync(string turma, EndChoice choice) => OneAtATime(async () =>
    {
        if (HomeClosed)
        {
            return choice == EndChoice.Keep
                ? await EndOnTheAgentsAsync(turma).ConfigureAwait(false)
                : throw new ClassFolderRefusedException(ClosedText);
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
        else if (reply?.Type == MessageType.RemoteClassFolderCommandReply
            && ClassFolderRemote.DecodeJson<RemoteReply>(reply.Payload) is { } decoded)
        {
            answer = decoded;
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

    private async Task<T> OneAtATime<T>(Func<Task<T>> command)
    {
        await _commands.WaitAsync().ConfigureAwait(false);
        try
        {
            return await command().ConfigureAwait(false);
        }
        finally
        {
            _commands.Release();
        }
    }

    private static string ChoiceOf(EndChoice choice) => choice switch
    {
        EndChoice.Keep => RemoteCommand.Keep,
        EndChoice.DeleteToday => RemoteCommand.Today,
        EndChoice.DeleteAll => RemoteCommand.All,
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };

    // The request, sent again (off the caller's thread) while the home turns it away.
    private async Task<Exchanged> RequestAsync(ClassHome home, Message request, TimeSpan wait)
    {
        for (var attempt = 1; ; attempt++)
        {
            var exchanged = await Task.Run(() => Exchange(home, request, wait)).ConfigureAwait(false);
            if (exchanged.Kind != Kind.TurnedAway || attempt == Attempts)
            {
                return exchanged;
            }
            await Task.Delay(AttemptPause).ConfigureAwait(false);
        }
    }

    // One request on its own connection. Unreachable: nobody took the
    // connection. TurnedAway: it was closed (or the home's certificate wasn't
    // the one pinned) before the request went, so it wasn't done. Lost: the
    // request went and no answer came back (it may have been done).
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
                channel = SecureChannel.AuthenticateAsClient(tcp, home.Address, _certificate, new TrustStore(new[] { home.Thumbprint }));
            }
            catch (Exception ex) when (IsNetwork(ex))
            {
                return new Exchanged(Kind.TurnedAway, null);
            }

            using (channel)
            {
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

    private static bool IsNetwork(Exception ex) =>
        ex is IOException or SocketException or AuthenticationException or InvalidDataException or ObjectDisposedException;

    private enum Kind
    {
        Answered,
        Unreachable,
        TurnedAway,
        Lost,
    }

    private readonly record struct Exchanged(Kind Kind, Message? Reply);
}
