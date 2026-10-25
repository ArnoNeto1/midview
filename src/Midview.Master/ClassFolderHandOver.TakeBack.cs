using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderHandOver: a take-over that can't be told done, settled through the
// old home (Ruling 21). An Agent that may have taken this PC's claim doesn't
// answer, so this PC asks the old home to take the room back
// (MessageType.RemoteTakeBack, with the generation it gave up): the old home,
// still the home, claims every Agent it reaches with a newer generation and
// keeps it. This PC then puts its own classes back and throws the copy away;
// the Agent that didn't answer, if it took the claim, keeps an older home than
// the old home's, and is taken back like any straggler. No PC runs on the
// copy. When the old home can't be reached, or refuses, the copy stays where
// it is (Ruling 20) until that Agent answers.
public sealed partial class ClassFolderHandOver
{
    // RemoteTakeBack to the old home (home, its certificate pinned); true when
    // it took the room back.
    private bool AskTheOldHomeToTakeBack(ClassHome home, long abandoned)
    {
        try
        {
            using var tcp = TcpConnect.WithTimeout(home.Address, home.Port, ConnectTimeout);
            tcp.ReceiveTimeout = tcp.SendTimeout = (int)NetworkWait.TotalMilliseconds;
            using var channel = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, home.Address, _certificate);
            if (!string.Equals(channel.RemoteThumbprint, home.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            channel.Send(new Message(MessageType.RemoteTakeBack, ClassFolderRemote.EncodeTakeBack(abandoned)));
            var reply = channel.Receive(ClassFolderRemote.MaxJsonBytes);
            var (tookBack, why) = reply.Type == MessageType.CommandAck ? CommandResult.Decode(reply.Payload) : (false, string.Empty);
            if (!tookBack)
            {
                _log($"The class folders' home {home.Name} didn't take the room back: {why}");
            }
            return tookBack;
        }
        catch (Exception ex) when (IsNetwork(ex) || ex is OperationCanceledException)
        {
            _log($"The class folders' home {home.Name} couldn't be asked to take the room back: {ex.Message}");
            return false;
        }
    }

    // On the home, for ClassFolderHost: a colleague's PC (checked by the host)
    // gave up a take-over at the generation abandoned. Only while this PC is
    // still the home (a generation of its own, and an online Agent naming it,
    // none naming another PC newer than abandoned): every online Agent is
    // claimed with a generation newer than abandoned, which this PC keeps. Ok
    // once one took it; otherwise why not, in the teacher's words.
    internal async Task<(bool Ok, string Detail)> TakeBackAsync(long abandoned)
    {
        long? own;
        try
        {
            own = ClassHomeFileMaster.Read(_homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            own = null;
        }

        var homes = own is null
            ? new List<ClassHome>()
            : await Task.Run(() => Homes(AgentsOnline()).Select(read => read.Home).OfType<ClassHome>().ToList()).ConfigureAwait(false);
        bool IsThisPc(ClassHome home) => home.IsSamePc(_certificate.Thumbprint, _machineName, own);
        if (own is null || !homes.Any(IsThisPc) || homes.Any(home => !IsThisPc(home) && home.Generation > abandoned))
        {
            _log("A colleague's PC asked this PC to take the class folders back, but this PC isn't their home any more.");
            return (false, ClassFolderText.NotTheHomeAnyMore(_machineName));
        }

        var claimed = await Task.Run(() => Claim(abandoned)).ConfigureAwait(false);
        if (claimed.Outcome != Outcome.Taken)
        {
            return (false, claimed.Detail.Length > 0 ? claimed.Detail : ClassFolderText.NoPcTookIt);
        }
        KeepGeneration(claimed.Generation);
        _log($"This PC took the class folders' room back with generation {claimed.Generation} (a take-over at {abandoned} was given up).");
        return (true, string.Empty);
    }
}
