using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderHost: what a colleague's request gets, from the home's controller.
public sealed partial class ClassFolderHost
{
    public const string IncompleteText = "O pedido chegou incompleto ao PC que guarda as pastas das turmas. Tente de novo.";

    public const string StillRunningText =
        "O PC que guarda as pastas das turmas ainda está fazendo isso. Veja o resultado na janela em instantes.";

    // Why something failed when Windows didn't say why in words of ours (ErrorText).
    public const string UnexpectedReason = "erro inesperado";

    private Message Answer(Message request)
    {
        switch (request.Type)
        {
            case MessageType.RemoteClassFolderState:
                RemoteState state;
                try
                {
                    // The view the home itself shows: the lesson's numbers while one is
                    // on, the class list otherwise (the controller decides, so a lesson
                    // ending meanwhile still comes with the list).
                    var read = _local.ReadAsync(lessonFiles: true);
                    if (!read.Wait(CommandTimeout))
                    {
                        return Refusal(ClassFolderRemote.UnreadableText + "demorou demais.");
                    }
                    state = read.Result;
                }
                catch (Exception ex)
                {
                    var cause = ex is AggregateException { InnerException: { } inner } ? inner : ex;
                    LogFailure("Reading the state", cause);
                    return Refusal(ClassFolderRemote.UnreadableText + Reason(cause));
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
                return Refusal(ClassFolderRemote.UnknownRequestText);
        }
    }

    // The command, as if clicked in the home's window; never throws. A class
    // created is named by Turma (the name asked for, which the store makes safe
    // or refuses); every other command's Turma must be a class name, or it is
    // a class that isn't there. The reply's Turma is the class it ended up
    // about (the folder's name a create or a rename gave it).
    private async Task<RemoteReply> RunAsync(RemoteCommand command)
    {
        var none = Array.Empty<ResultLineDto>();
        var turma = command.Turma;
        if (command.Kind != RemoteCommand.Create && turma is not null && !ClassStore.IsValidName(turma))
        {
            return new RemoteReply(false, ClassFolderText.ClassGone, none, null);
        }
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
        catch (ArgumentException) when (command.Kind == RemoteCommand.Start)
        {
            // Its folder deleted in Explorer just as the lesson was starting.
            return new RemoteReply(false, ClassFolderText.ClassGone, none, turma);
        }
        catch (Exception ex)
        {
            // What the home's own window says when that fails.
            LogFailure($"A colleague's {command.Kind}", ex);
            var reason = Reason(ex);
            var detail = command.Kind switch
            {
                RemoteCommand.Start => ClassFolderText.StartFailed(turma ?? string.Empty, reason),
                RemoteCommand.End => ClassFolderText.EndFailed(turma ?? string.Empty, reason),
                RemoteCommand.Delete => ClassFolderText.DeleteFailed(reason),
                _ => reason,
            };
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

    // ErrorText's words for what Windows said (disk full, access denied...);
    // never an exception's own message, which may be in English or say too much.
    private static string Reason(Exception ex)
    {
        var described = ErrorText.Describe(ex);
        return described == ex.Message ? UnexpectedReason : described;
    }

    // The disk saying no is expected (its message is enough); anything else is a mistake to look into.
    private void LogFailure(string what, Exception ex) =>
        _log(ex is IOException or UnauthorizedAccessException ? $"{what} failed: {ex.Message}" : $"{what} failed: {ex}");

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
}
