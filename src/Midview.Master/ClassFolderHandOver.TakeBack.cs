using System.Globalization;
using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderHandOver: a take-over that can't be told done, settled through the
// old home (Ruling 21). An Agent that may have taken this PC's claim doesn't
// answer, so this PC asks the old home to take the room back
// (MessageType.RemoteTakeBack, with the generation it gave up): the old home,
// still the home, claims every Agent it reaches with a newer generation and
// keeps it. This PC then puts its own classes back, throws the copy away and
// keeps the old home, at that generation, as the home it saw (Ruling 22: with
// only that Agent on, naming this PC, this PC still goes by the old home);
// the Agent that didn't answer, if it took the claim, keeps an older home than
// the old home's, and is taken back like any straggler. No PC runs on the
// copy. When the old home can't be reached, or refuses, the copy stays where
// it is (Ruling 20) until that Agent answers.
//
// The old home is still the home (Ruling 22) while it handed nothing over
// since it was, an online Agent names it, and neither an online Agent nor the
// last other home it saw names another PC newer than it (the PC asking, only
// when newer than what it gave up). It believes
// a generation given up no further ahead of its own clock than a day. It
// takes the room back one thing at a time with a hand-over (never both), and
// starts claiming within TakeBackClaimBy of being asked or not at all, so the
// PC asking always gets its answer (it waits TakeBackWait).
public sealed partial class ClassFolderHandOver
{
    // How long the PC giving a take-over up waits for the old home's answer
    // (TCP keep-alive notices a home switched off meanwhile): longer than the
    // host's CommandTimeout, so the old home always answers first.
    internal static readonly TimeSpan TakeBackWait = TimeSpan.FromMinutes(6);

    // The latest a take-back starts claiming the Agents after it was asked
    // (waiting for a hand-over in progress, reading which home the Agents
    // keep); later, it is refused. The claims, a lost answer's wait and the
    // Agents asked again then take a few minutes at the most, well within the
    // host's CommandTimeout and TakeBackWait.
    internal static readonly TimeSpan TakeBackClaimBy = TimeSpan.FromMinutes(1);

    // A generation given up from further ahead of this PC's clock than this is not believed.
    private static readonly TimeSpan TakeBackHorizon = TimeSpan.FromDays(1);

    // One take-back or hand-over at a time, on the old home.
    private readonly SemaphoreSlim _giving = new(1, 1);

    // This PC handed the folders over (it stood down) and hasn't taken them
    // over again since: it takes no room back. Under _giving, or volatile.
    private volatile bool _handedOver;

    // RemoteTakeBack to the old home (home, its certificate pinned): the
    // generation it took the room back with, or null when it didn't.
    private long? AskTheOldHomeToTakeBack(ClassHome home, long abandoned)
    {
        try
        {
            using var tcp = TcpConnect.WithTimeout(home.Address, home.Port, ConnectTimeout);
            tcp.ReceiveTimeout = tcp.SendTimeout = (int)NetworkWait.TotalMilliseconds;
            using var channel = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, home.Address, _certificate);
            if (!string.Equals(channel.RemoteThumbprint, home.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            channel.Send(new Message(MessageType.RemoteTakeBack, ClassFolderRemote.EncodeTakeBack(abandoned)));
            // The old home's claims may take minutes: its answer is waited for.
            tcp.ReceiveTimeout = (int)TakeBackWait.TotalMilliseconds;
            CommandClient.EnableKeepAlive(tcp.Client);
            var reply = channel.Receive(ClassFolderRemote.MaxJsonBytes);
            var (tookBack, why) = reply.Type == MessageType.CommandAck ? CommandResult.Decode(reply.Payload) : (false, string.Empty);
            if (!tookBack)
            {
                _log($"The class folders' home {home.Name} didn't take the room back: {why}");
                return null;
            }
            // Newer than what this PC gave up, whatever it says.
            var floor = abandoned == long.MaxValue ? abandoned : abandoned + 1;
            return long.TryParse(why, NumberStyles.None, CultureInfo.InvariantCulture, out var generation) && generation > floor
                ? generation
                : floor;
        }
        catch (Exception ex) when (IsNetwork(ex) || ex is OperationCanceledException)
        {
            _log($"The class folders' home {home.Name} couldn't be asked to take the room back: {ex.Message}");
            return null;
        }
    }

    // The old home took the room back at that generation: the home this PC saw (ClassHomeFileMaster).
    private void RememberTheOldHome(ClassHome home)
    {
        try
        {
            ClassHomeFileMaster.WriteLastOtherHome(home, _homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log($"The class folders' home that took the room back could not be saved as the one seen: {ex.Message}");
        }
    }

    // On the home, for ClassFolderHost: a colleague's PC (sender: its
    // certificate's thumbprint, checked by the host) gave up a take-over at
    // the generation abandoned. Only while this PC is still the home (see the
    // class comment): every online Agent is claimed with a generation newer
    // than abandoned and than this PC's own, which this PC keeps. Ok once one
    // took it, its detail that generation; otherwise why not, in the teacher's words.
    internal async Task<(bool Ok, string Detail)> TakeBackAsync(long abandoned, string sender)
    {
        var askedAt = Milliseconds();
        if (abandoned > new DateTimeOffset(_localNow()).UtcTicks + TakeBackHorizon.Ticks)
        {
            _log($"A colleague's PC asked this PC to take the class folders back from generation {abandoned}, too far ahead of its clock.");
            return (false, ClassFolderText.TakeBackFromTooFarAhead(_machineName));
        }
        if (!await _giving.WaitAsync(TakeBackClaimBy).ConfigureAwait(false))
        {
            _log("A colleague's PC asked this PC to take the class folders back while it handed them over.");
            return (false, ClassFolderText.TakeBackTooSlow(_machineName));
        }
        try
        {
            return await Task.Run(() => TakeBack(abandoned, sender, askedAt)).ConfigureAwait(false);
        }
        finally
        {
            _giving.Release();
        }
    }

    // TakeBackAsync's work, under _giving, on a thread of its own.
    private (bool Ok, string Detail) TakeBack(long abandoned, string sender, long askedAt)
    {
        long? saved;
        try
        {
            saved = ClassHomeFileMaster.Read(_homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            saved = null;
        }
        var homes = Homes(AgentsOnline()).Select(read => read.Home).OfType<ClassHome>().ToList();

        // The newest of the one on file and the ones the Agents keep for this PC (as a start goes by: Ruling 21).
        var recorded = homes.Where(home => home.IsSamePc(_certificate.Thumbprint, _machineName, saved))
            .Select(home => (long?)home.Generation).Where(generation => generation > 0).Max();
        var own = saved is { } kept && recorded is { } fromAgents ? Math.Max(kept, fromAgents) : saved ?? recorded;
        bool IsThisPc(ClassHome home) => home.IsSamePc(_certificate.Thumbprint, _machineName, own);
        bool IsSender(ClassHome home) => string.Equals(home.Thumbprint, sender, StringComparison.OrdinalIgnoreCase);

        // Another PC newer than this one, as an Agent keeps it or as this PC
        // saw it (ClassHomeFileMaster): the PC asking only past what it gave up.
        bool Newer(ClassHome home, long generation) =>
            !IsThisPc(home) && (home.Generation > abandoned || (!IsSender(home) && home.Generation > generation));
        if (_handedOver || own is not { } generation || !homes.Any(IsThisPc)
            || homes.Any(home => Newer(home, generation)) || (SeenHome() is { } seen && Newer(seen, generation)))
        {
            _log("A colleague's PC asked this PC to take the class folders back, but this PC isn't their home any more.");
            return (false, ClassFolderText.NotTheHomeAnyMore(_machineName));
        }
        if (Milliseconds() - askedAt > TakeBackClaimBy.TotalMilliseconds)
        {
            _log("A colleague's PC asked this PC to take the class folders back, but the room's PCs took too long to answer.");
            return (false, ClassFolderText.TakeBackTooSlow(_machineName));
        }

        var claimed = Claim(Math.Max(abandoned, generation), retryBusy: true, homes);
        if (claimed.Outcome != Outcome.Taken)
        {
            return (false, claimed.Detail.Length > 0 ? claimed.Detail : ClassFolderText.NoPcTookIt);
        }
        KeepGeneration(claimed.Generation);
        _log($"This PC took the class folders' room back with generation {claimed.Generation} (a take-over at {abandoned} was given up).");
        return (true, claimed.Generation.ToString(CultureInfo.InvariantCulture));
    }
}
