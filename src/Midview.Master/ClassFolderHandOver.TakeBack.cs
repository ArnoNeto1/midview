using System.Globalization;
using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// ClassFolderHandOver: a take-over that can't be told done, settled through the
// old home (left open, the room could later move to a stale copy while newer
// work stays on the old home). An Agent that may have taken this PC's claim
// doesn't answer, so this PC asks the old home to take the room back
// (MessageType.RemoteTakeBack, with the generation it gave up): the old home,
// still the home, claims every Agent it reaches with a newer generation and
// keeps it. This PC then puts its own classes back, throws the copy away and
// keeps the old home, at that generation, as the home it saw (with only that
// Agent on, naming this PC, this PC still goes by the old home);
// the Agent that didn't answer, if it took the claim, keeps an older home than
// the old home's, and is taken back like any straggler. No PC runs on the
// copy. When the old home can't be reached, or refuses, the copy stays where
// it is until that Agent answers (nothing is lost either way).
//
// The old home is still the home while no hand-over of its counts
// (ClassFolderHandOver.Busy.cs: kept on file, also once its Master is opened
// again), an online Agent names it, and neither an online Agent nor the
// last other home it saw names another PC newer than it (the PC asking, only
// when newer than what it gave up). It believes a generation given up no
// further ahead than a day of its clock, or of its own generation when the
// room is ahead of the clock; the PC asking believes the same of the one it
// is answered with (a bogus one would put every later take-over out of
// reach). It takes the room back one thing at a time with a hand-over (never both), and
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
            // Newer than what this PC gave up, whatever it says, and no further
            // ahead than a day (of this PC's clock, or of what it gave up when
            // that is later) nor past the latest generation: a bogus answer
            // kept as the home seen would put every later take-over out of reach.
            var floor = ClassHome.Next(abandoned);
            var ceiling = Math.Min(
                ClassHome.MaxGeneration, Plus(Math.Max(new DateTimeOffset(_localNow()).UtcTicks, abandoned), TakeBackHorizon));
            return long.TryParse(why, NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
                && generation > floor && generation <= ceiling
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
    // Whatever the answer, the copy that PC took has settled (ClassFolderHandOver.Busy.cs).
    internal async Task<(bool Ok, string Detail)> TakeBackAsync(long abandoned, string sender)
    {
        try
        {
            return await TakeBackWhenFreeAsync(abandoned, sender).ConfigureAwait(false);
        }
        finally
        {
            Settled(sender);
        }
    }

    private async Task<(bool Ok, string Detail)> TakeBackWhenFreeAsync(long abandoned, string sender)
    {
        var askedAt = Milliseconds();
        var saved = await Task.Run(SavedGeneration).ConfigureAwait(false);
        // A day past this PC's clock, or past its own generation when the room is ahead of the clock.
        if (abandoned > Plus(Math.Max(new DateTimeOffset(_localNow()).UtcTicks, saved ?? 0), TakeBackHorizon))
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
        var saved = SavedGeneration();
        var homes = Homes(AgentsOnline()).Select(read => read.Home).OfType<ClassHome>().ToList();

        // As a start goes by it: a take-over this PC made that its file doesn't have counts.
        var own = ClassHomeFileMaster.OwnGeneration(saved, homes, _certificate.Thumbprint, _machineName);
        bool IsThisPc(ClassHome home) => home.IsSamePc(_certificate.Thumbprint, _machineName, own);
        bool IsSender(ClassHome home) => string.Equals(home.Thumbprint, sender, StringComparison.OrdinalIgnoreCase);

        // Another PC newer than this one, as an Agent keeps it or as this PC
        // saw it (ClassHomeFileMaster): the PC asking only past what it gave up.
        bool Newer(ClassHome home, long generation) =>
            !IsThisPc(home) && (home.Generation > abandoned || (!IsSender(home) && home.Generation > generation));

        // A hand-over of this PC's counts until the room names it above the
        // generation it was at; one that can't be told (the file unread) counts:
        // a take-back refused leaves the PC asking with both copies, one taken by
        // an old home that gave its folders away would leave the room on an empty one.
        var (handedOverTo, unread) = HandedOverStanding();
        if (handedOverTo is { } to && homes.Any(home => IsThisPc(home) && home.Generation > to.Generation))
        {
            _log($"The room names this PC the class folders' home above its hand-over to {to.Name}: that no longer counts.");
            KeepHandedOver(null);
            handedOverTo = null;
        }
        if (handedOverTo is not null || unread || own is not { } generation || !homes.Any(IsThisPc)
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

    // This PC's generation on file; null for none, or one that can't be read.
    private long? SavedGeneration()
    {
        try
        {
            return ClassHomeFileMaster.Read(_homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ticks later by span, at most long.MaxValue.
    private static long Plus(long ticks, TimeSpan span) => ticks > long.MaxValue - span.Ticks ? long.MaxValue : ticks + span.Ticks;
}
