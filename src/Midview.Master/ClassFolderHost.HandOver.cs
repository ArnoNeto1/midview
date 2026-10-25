using System.Net.Sockets;
using System.Text;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderHost: a colleague's PC taking the class folders over (plan decision
// 7, Ruling 4), through this PC's ClassFolderHandOver. The copy is long: each
// write to the colleague's PC may take HandOverTimeout (not the state read's
// bound) and the whole has no limit; TCP keep-alive notices a PC switched off,
// and closing the host breaks the copy off. It is one connection of the
// MaxPerAddress a PC may have, so that PC's window still reads the state meanwhile.
public sealed partial class ClassFolderHost
{
    // How long one write of the copy to the colleague's PC may take.
    internal TimeSpan HandOverTimeout { get; set; } = TimeSpan.FromMinutes(2);

    // RemoteHandOver: the store, as ClassFolderHandOver.Send streams it.
    private void SendTheFolders(TcpClient client, SecureChannel channel)
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
        _handOver.Send(channel, _stop.Token);
    }

    // RemoteHandOverDone: the colleague's PC (its computer name, UTF-8) took the
    // folders over, and this PC stands down if the room's Agents say so.
    private Message HandedOver(Message request, string thumbprint)
    {
        if (_handOver is null)
        {
            return Refusal(ClassFolderRemote.UnknownRequestText);
        }

        string name;
        try
        {
            name = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(request.Payload);
        }
        catch (DecoderFallbackException)
        {
            return Refusal(IncompleteText);
        }
        if (name.Length is 0 or > ClassHome.MaxNameLength)
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
