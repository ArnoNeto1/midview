using System.Net.Sockets;
using System.Text;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderHost: a colleague's PC taking the class folders over (plan decision
// 7), through this PC's ClassFolderHandOver. The copy is long: each
// write to the colleague's PC may take HandOverTimeout (not the state read's
// bound) and the whole has no limit; TCP keep-alive notices a PC switched off,
// and closing the host breaks the copy off. It is one connection of the
// MaxPerAddress a PC may have, so that PC's window still reads the state meanwhile.
public sealed partial class ClassFolderHost
{
    // How long one write of the copy to the colleague's PC may take.
    internal TimeSpan HandOverTimeout { get; set; } = TimeSpan.FromMinutes(2);

    // RemoteHandOver (the colleague's PC's computer name, UTF-8: only for the
    // teacher's eyes, so one that isn't a name is none): the store, as
    // ClassFolderHandOver.Send streams it.
    private void SendTheFolders(TcpClient client, SecureChannel channel, Message request)
    {
        if (_handOver is null)
        {
            channel.Send(Refusal(ClassFolderRemote.UnknownRequestText));
            return;
        }

        var wait = (int)HandOverTimeout.TotalMilliseconds;
        client.SendTimeout = wait;
        client.ReceiveTimeout = wait;
        CommandClient.EnableKeepAlive(client.Client);
        _handOver.Send(channel, _stop.Token, NameIn(request.Payload) ?? string.Empty);
    }

    // A computer name in UTF-8 (1 to ClassHome.MaxNameLength characters); null for anything else.
    private static string? NameIn(byte[] payload)
    {
        try
        {
            var name = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(payload);
            return name.Length is 0 or > ClassHome.MaxNameLength ? null : name;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    // RemoteTakeBack: the colleague's PC (its certificate's thumbprint) gave up
    // a take-over it can't tell done (the generation it claimed with), and this
    // PC claims the room back if it is still the home
    // (ClassFolderHandOver.TakeBackAsync, which starts claiming within a
    // minute or not at all, so its answer comes well before CommandTimeout).
    private Message TakenBack(Message request, string thumbprint)
    {
        if (_handOver is null)
        {
            return Refusal(ClassFolderRemote.UnknownRequestText);
        }
        if (!ClassFolderRemote.TryDecodeTakeBack(request.Payload, out var abandoned))
        {
            return Refusal(IncompleteText);
        }

        try
        {
            var takenBack = _handOver.TakeBackAsync(abandoned, thumbprint);
            if (!takenBack.Wait(CommandTimeout))
            {
                return Refusal(StillRunningText);
            }
            var (ok, detail) = takenBack.Result;
            return new Message(MessageType.CommandAck, CommandResult.Encode(ok, detail));
        }
        catch (Exception ex)
        {
            var cause = ex is AggregateException { InnerException: { } inner } ? inner : ex;
            LogFailure("Taking the room back for a colleague's PC", cause);
            return Refusal(Reason(cause));
        }
    }

    // RemoteHandOverDone: the colleague's PC (its computer name, UTF-8) took the
    // folders over, and this PC stands down if the room's Agents say so.
    private Message HandedOver(Message request, string thumbprint)
    {
        if (_handOver is null)
        {
            return Refusal(ClassFolderRemote.UnknownRequestText);
        }

        if (NameIn(request.Payload) is not { } name)
        {
            return Refusal(IncompleteText);
        }

        try
        {
            var handedOver = _handOver.HandedOverAsync(name, thumbprint);
            if (!handedOver.Wait(CommandTimeout))
            {
                return Refusal(StillRunningText);
            }
            var (ok, detail) = handedOver.Result;
            return new Message(MessageType.CommandAck, CommandResult.Encode(ok, detail));
        }
        catch (Exception ex)
        {
            var cause = ex is AggregateException { InnerException: { } inner } ? inner : ex;
            LogFailure("Standing down for a colleague's PC", cause);
            return Refusal(Reason(cause));
        }
    }
}
