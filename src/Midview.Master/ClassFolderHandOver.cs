using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Protocol;

namespace Midview.Master;

// What a take-over came to, in the teacher's words (the window shows Detail as it is).
public sealed record HandOverResult(bool Ok, string Detail);

// "Cancelar" for a copy of the class folders (ClassFolderHandOver.TakeOverAsync):
// a cancel and the copy going in place exclude each other, and
// whichever comes first wins, at once, so the window knows right away whether
// a click stopped anything.
public sealed class HandOverCancel
{
    private const int Open = 0;
    private const int Cancelled = 1;
    private const int Committed = 2;

    private readonly CancellationTokenSource _source = new();
    private int _state;

    // Whether a cancel stopped the copy.
    public bool IsCancelled => Volatile.Read(ref _state) == Cancelled;

    internal CancellationToken Token => _source.Token;

    // Stops the copy: true when it stops (nothing changes, or nothing will),
    // false when it is too late (the copy is going in place, and the take-over
    // goes on to the end).
    public bool Cancel()
    {
        var was = Interlocked.CompareExchange(ref _state, Cancelled, Open);
        if (was == Open)
        {
            _source.Cancel();
        }
        return was != Committed;
    }

    // The copy goes in place, unless it was cancelled first (false).
    internal bool Commit() => Interlocked.CompareExchange(ref _state, Committed, Open) != Cancelled;
}

// How far a copy has come: the files and bytes that arrived, of all of them.
// InPlace: the copy is whole and in place, and the room's PCs are being told;
// from here on the take-over can't be cancelled.
public readonly record struct HandOverProgress(int Files, int TotalFiles, long Bytes, long TotalBytes, bool InPlace = false);

// "Guardar as pastas das turmas neste computador" ("Two teacher PCs in one
// lab", docs/superpowers/specs/2026-10-20-class-folder-design.md; plan
// decision 7, Review Focus 5): this teacher PC takes the
// class folders over from the PC that keeps them (the home), or, as that home,
// gives them. One per Master, for its store (ClassStore) and sync (ClassFolderSync).
//
// Taking them over from a home that answers (TakeOverAsync): the home's whole
// store comes over its ClassFolderHost into Staging (which must be gone first:
// what an earlier take-over left there is never taken in), every path checked
// to stay inside it and every size against what was announced, and all of it
// must fit on this PC's disk with KeepFreeBytes to spare. Nothing changes on
// either PC or on the Agents until all of it is there and checks out; any
// failure before then, or the teacher cancelling, throws the copy away. Then
// this PC's own earlier store goes aside to "Turmas (antes de <dd-MM-yyyy
// HHmm>)", the copy goes in its place (from here on, cancelling does nothing),
// and every online Agent is claimed with a take-over (ClassFolderHandOver.
// Claim.cs). When none of them took it (a lesson on, say), everything is put
// back as it was. When one did, this PC's generation is kept
// (ClassHomeFileMaster), and the old home is told (MessageType.
// RemoteHandOverDone): it stands down and moves its copy aside to "Turmas
// (passadas para <this PC>)", or, with a lesson on, keeps the folders until
// that lesson ends. An old home that doesn't answer that finds out by itself
// (ClassHomeTracker) and keeps its copy where it is.
//
// Taking them over from a home that doesn't answer (TakeOverUnreachableAsync,
// after the window's confirmation): the Agents are claimed the same way, and
// this PC goes on with its own store, or none, with the lessons the room
// follows remembered and their classes made (LearnTheRoomsLessons), so what
// the PCs set aside for them comes in at this PC's next lesson.
//
// Giving them, on the home (for ClassFolderHost): Send streams the store
// (ClassFolderHandOver.Send.cs) and HandedOverAsync stands down once the room's
// Agents keep the sender as the home. Never while a lesson is on.
//
// On the wire (ClassFolderRemote.Port, after the host's checks), RemoteHandOver
// (the taking PC's computer name, UTF-8, which the old home only shows) is
// answered with one CommandAck refusing it (CommandResult false, why), or
// with the store: first ManifestPath (JSON: every class, and every file that
// follows, in order, with its size and last write time in UTC ticks), then each
// of those files as a SendFile header (FileTransfer.EncodeHeader: the size, then
// the path: "files/<turma>/<path>", "index/<turma>.json" or "lessons.json") and
// FileChunks adding up to exactly that size, and last one CommandAck (true,
// "<n> arquivos"). A CommandAck in the middle says why the rest won't come.
// RemoteHandOverDone (the new home's computer name, UTF-8) is answered with one
// CommandAck: true once the old home stood down, its detail the folder its copy
// went to (empty: it had none; NotMovedMark and why: it couldn't go aside, and
// is where it was; PartlyMovedMark and the folder: its classes went there but
// not all that goes with them, and couldn't come back); false, with why in the
// teacher's words, when it doesn't stand down (the room's Agents don't keep
// the sender as the home, or a lesson is on there).
//
// A take-over whose claim can't be told taken or not (an Agent that may have
// taken it doesn't answer: Claim's Unknown) asks the old home to take the room
// back (ClassFolderHandOver.TakeBack.cs), then puts this PC's own
// classes back and throws the copy away. Only when the old home can't be
// reached or refuses does the copy stay in place, this PC's own classes
// aside, the old home untold and no generation of its own kept: when that PC
// is back, what the room's Agents keep decides. One naming this PC (its
// certificate and name) makes it the home with the copy (ClassHomeTracker, and
// a start goes by the generation the Agents recorded for it); all naming the
// old home leave it a remote, and a start of its own is refused as one from a
// PC that was never the home (it keeps no generation of its own, so its
// next start can't take the room without an Agent). An unreachable home's
// take-over has no copy to give back and nobody to ask: undecided, it leaves this PC's own
// classes as they are, keeps no generation, and the room's Agents decide.
//
// In six files: this one (taking over), ClassFolderHandOver.Claim.cs (the
// Agents), ClassFolderHandOver.TakeBack.cs (an undecided take-over given back),
// ClassFolderHandOver.Send.cs (giving, on the home),
// ClassFolderHandOver.Receive.cs (the copy arriving, checked) and
// ClassFolderHandOver.Busy.cs (what the Master goes by while one runs, and after).
public sealed partial class ClassFolderHandOver
{
    // What a take-over leaves free on this PC's disk at least: Windows and the PCs' files need room too.
    public const long KeepFreeBytes = 1L << 30;

    // Start RemoteHandOverDone's detail when the old home's copy couldn't go
    // aside (ErrorText's words follow), or went only partly (its folder
    // follows): never in a folder name.
    internal const char NotMovedMark = '|';

    internal const char PartlyMovedMark = '*';

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly ClassStore _store;
    private readonly ClassFolderSync _sync;
    private readonly X509Certificate2 _certificate;
    private readonly string _machineName;
    private readonly Func<IReadOnlyList<KnownMachine>> _machines;
    private readonly Func<KnownMachine, bool> _isOnline;
    private readonly Func<DateTime> _localNow;
    private readonly int _agentPort;
    private readonly int _homePort;
    private readonly string _homePath;
    private readonly Action<string> _log;

    // A take-over is running (1).
    private int _taking;

    // machineName: this PC's computer name (Environment.MachineName), which
    // the claims carry, with homePort (where this Master listens for a
    // colleague's PC, ClassFolderRemote.Port). machines and isOnline: the
    // classroom list and the grid's view of it, called from background threads.
    // homePath: this PC's generation as the home (ClassHomeFileMaster.Path).
    // log: what went wrong that the teacher isn't told, in English, by default
    // to System.Diagnostics.Trace.
    public ClassFolderHandOver(
        ClassStore store, ClassFolderSync sync, X509Certificate2 masterCertificate, string machineName,
        Func<IReadOnlyList<KnownMachine>> machines, Func<KnownMachine, bool> isOnline, Func<DateTime>? localNow = null,
        int agentPort = ConnectionClient.AgentPort, int homePort = ClassFolderRemote.Port, string? homePath = null,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(machineName);
        if (machineName.Length > ClassHome.MaxNameLength)
        {
            throw new ArgumentException($"A computer name is at most {ClassHome.MaxNameLength} characters.", nameof(machineName));
        }

        _store = store;
        _sync = sync;
        _certificate = masterCertificate;
        _machineName = machineName;
        _machines = machines;
        _isOnline = isOnline;
        _localNow = localNow ?? (() => DateTime.Now);
        _agentPort = agentPort;
        _homePort = homePort;
        _homePath = homePath ?? ClassHomeFileMaster.Path;
        _log = log ?? (message => System.Diagnostics.Trace.WriteLine("ClassFolderHandOver: " + message));
        // A hand-over an earlier run made (one on a file unread now is read again when it matters).
        HandedOverStanding();
    }

    // How far a take-over got, for what to say when it breaks off.
    private enum Stage
    {
        // Nothing has changed yet (the copy is still arriving, or being checked).
        Copying,

        // The copy is in place, and the room's Agents are being claimed.
        Claiming,

        // The Agents took this PC as the home; the old home is being told.
        Claimed,
    }

    // This PC's computer name, which the old home's copy is named after.
    public string MachineName => _machineName;

    // How long one read or write on the network may take during a hand-over.
    internal static readonly TimeSpan DefaultNetworkWait = TimeSpan.FromMinutes(2);

    internal TimeSpan NetworkWait { get; set; } = DefaultNetworkWait;

    // How many classes this PC has of its own now (they go aside in a take-over); 0 when that can't be read.
    public int OwnClassCount()
    {
        try
        {
            return _store.ClassNames().Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    // The home's class folders, to this PC (see the class comment). home: where
    // the room's Agents say it is, its certificate pinned. progress: as the copy
    // arrives, on the copy's thread. cancel: stops the copy, and changes
    // nothing, until it goes in place (HandOverCancel.Cancel says which). Never throws.
    public Task<HandOverResult> TakeOverAsync(
        ClassHome home, IProgress<HandOverProgress>? progress = null, HandOverCancel? cancel = null)
    {
        var name = home.Name.Length > 0 ? home.Name : ClassHome.UnnamedText;
        return Alone(
            stage => TakeOver(home, name, progress, cancel, stage),
            (stage, reason) => stage.Value switch
            {
                Stage.Copying => new HandOverResult(false, ClassFolderText.TakeOverFailed(name, reason)),
                Stage.Claiming => new HandOverResult(false, ClassFolderText.TakeOverBrokeOffClaiming(name, reason)),
                _ => new HandOverResult(true, ClassFolderText.TakeOverBrokeOffTellingTheOldHome(name, reason)),
            });
    }

    // The home PC (named home in the texts) doesn't answer: the Agents take
    // this PC as the home, and its own store (or none) is the one now. Never throws.
    public Task<HandOverResult> TakeOverUnreachableAsync(string home) => Alone(
        stage =>
        {
            stage.Value = Stage.Claiming;
            var claimed = Claim();
            if (claimed.Outcome == Outcome.NotTaken)
            {
                return new HandOverResult(false, ClassFolderText.TakeOverUnreachableFailed(claimed.Detail));
            }
            if (claimed.Outcome == Outcome.Unknown)
            {
                // No generation of its own: see the class comment.
                return new HandOverResult(false, ClassFolderText.TakeOverUnreachableUndecided(claimed.Unknown));
            }
            stage.Value = Stage.Claimed;
            KeepGeneration(claimed.Generation);
            KeepHandedOver(null);
            KeepTakenOverFrom(claimed.Replaced);
            _log($"This PC took the class folders over from {home}, which didn't answer, with generation {claimed.Generation}.");
            LearnTheRoomsLessons();
            return new HandOverResult(true, ClassFolderText.TookOverUnreachable);
        },
        (stage, reason) => stage.Value == Stage.Copying
            ? new HandOverResult(false, ClassFolderText.TakeOverUnreachableFailed(reason))
            : new HandOverResult(stage.Value == Stage.Claimed, ClassFolderText.TakeOverUnreachableBrokeOff(reason)));

    // One take-over at a time, on its own thread (it blocks on the network and
    // the disk), with no lesson starting on this PC meanwhile and none on when
    // it begins (ClassFolderSync.BeginTakeOver; with one on, it is refused as
    // nothing changed: brokeOff's words at Stage.Copying). brokeOff: what to
    // say when it throws (a mistake in the code: nothing in it throws for what
    // the network, the disk or a PC does), by the stage it got to; a copy not
    // in place yet is thrown away.
    private Task<HandOverResult> Alone(Func<StrongBox<Stage>, HandOverResult> takeOver, Func<StrongBox<Stage>, string, HandOverResult> brokeOff)
    {
        if (Interlocked.Exchange(ref _taking, 1) == 1)
        {
            return Task.FromResult(new HandOverResult(false, ClassFolderText.TakeOverRunning));
        }
        return Task.Factory.StartNew(
            () =>
            {
                var stage = new StrongBox<Stage>(Stage.Copying);
                var holding = false;
                try
                {
                    if (!_sync.BeginTakeOver())
                    {
                        return brokeOff(stage, ClassFolderText.EndLessonFirst);
                    }
                    holding = true;
                    return takeOver(stage);
                }
                catch (Exception ex)
                {
                    if (stage.Value == Stage.Copying)
                    {
                        _store.DiscardStaging();
                    }
                    try
                    {
                        _log($"Taking the class folders over broke off ({stage.Value}): {ex}");
                    }
                    catch (Exception)
                    {
                        // What went wrong may be the log itself.
                    }
                    return brokeOff(stage, ErrorText.Describe(ex));
                }
                finally
                {
                    if (holding)
                    {
                        _sync.EndTakeOver();
                    }
                    Volatile.Write(ref _taking, 0);
                    Quiet();
                }
            },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private HandOverResult TakeOver(
        ClassHome home, string name, IProgress<HandOverProgress>? progress, HandOverCancel? cancel, StrongBox<Stage> stage)
    {
        _store.DiscardStaging();
        if (_store.StagingLeft() is { } left)
        {
            return new HandOverResult(false, ClassFolderText.TakeOverFailed(name, ClassFolderText.StagingLeft(Path.GetFileName(left))));
        }

        var received = Receive(home, name, progress, cancel?.Token ?? CancellationToken.None);
        if (received.Failure is null && cancel is not null && !cancel.Commit())
        {
            received = Received.Failed(ClassFolderText.CopyCancelled);
        }
        if (received.Failure is { } failure)
        {
            _store.DiscardStaging();
            return new HandOverResult(false, ClassFolderText.TakeOverFailed(name, failure));
        }

        IHandOverSwap swap;
        try
        {
            swap = _store.SwapIn(BeforeWhy());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"The class folders received could not be put in place: {ex.Message}");
            _store.DiscardStaging();
            var putBack = (ex as StoreMoveException)?.PutBack ?? new PutBack(true, false, null);
            return new HandOverResult(
                false, NotTaken(name, ClassFolderText.CopyNotSaved(ErrorText.Describe(ex.InnerException ?? ex)), putBack));
        }

        stage.Value = Stage.Claiming;
        progress?.Report(new HandOverProgress(received.Files, received.Files, 0, 0, InPlace: true));
        var claimed = Claim();
        var ownAside = swap.OwnStoreAside is { } own ? Path.GetFileName(own) : null;
        if (claimed.Outcome == Outcome.NotTaken)
        {
            var putBack = swap.Undo();
            _store.DiscardStaging();
            return new HandOverResult(false, NotTaken(name, claimed.Detail, putBack));
        }
        if (claimed.Outcome == Outcome.Unknown)
        {
            // The old home takes the room back, then this PC is as it was
            // (ClassFolderHandOver.TakeBack.cs); if it can't, nothing is put
            // back, nobody told, no generation of its own (see the class comment).
            if (AskTheOldHomeToTakeBack(home, claimed.Generation) is { } takenBackAt)
            {
                RememberTheOldHome(home with { Generation = takenBackAt });
                var putBack = swap.Undo();
                _store.DiscardStaging();
                var takenBack = ClassFolderText.TakeOverTakenBack(name, claimed.Unknown);
                return new HandOverResult(false, putBack.All
                    ? takenBack
                    : ClassFolderText.TakeOverNotPutBack(
                        name, takenBack, putBack.CopyStayed, putBack.OwnAside is { } aside ? Path.GetFileName(aside) : null));
            }
            _log($"The class folders from {name} stay in place until {string.Join(", ", claimed.Unknown)} can say which PC keeps them.");
            return new HandOverResult(false, ClassFolderText.TakeOverUndecided(name, claimed.Unknown, ownAside));
        }
        stage.Value = Stage.Claimed;
        KeepGeneration(claimed.Generation);
        KeepHandedOver(null);
        KeepTakenOverFrom(null); // the old home's lessons came with the copy
        _log($"This PC took the class folders over from {name} with generation {claimed.Generation}.");

        var text = new StringBuilder(ClassFolderText.TookOver(received.Classes, received.Files));
        if (ownAside is not null)
        {
            text.Append(' ').Append(ClassFolderText.OwnClassesAside(ownAside));
        }
        var (said, where) = WhatTheOldHomeSaid(home, name);
        text.Append(' ').Append(said);
        if (received.LeftOut > 0)
        {
            // The files its store doesn't list (hidden, too big, odd names): only in its copy, wherever that is.
            text.Append(' ').Append(ClassFolderText.LeftOnTheOldHome(name, received.LeftOut, where));
        }
        return new HandOverResult(true, text.ToString());
    }

    // A take-over that didn't stand (why), with what was put back: "nada
    // mudou" only when all of it was.
    private static string NotTaken(string name, string why, PutBack putBack) => putBack.All
        ? ClassFolderText.TakeOverFailed(name, why)
        : ClassFolderText.TakeOverNotPutBack(
            name, why, putBack.CopyStayed, putBack.OwnAside is { } aside ? Path.GetFileName(aside) : null);

    // RemoteHandOverDone to the old home, and its answer in the teacher's
    // words (Said), with the folder its copy is in there (Where: "Turmas"
    // when it stayed; null for one whose name can't be shown). Which case it
    // is goes by what that PC said; only what is shown of it is made one
    // short line (it can say anything).
    private (string Said, string? Where) WhatTheOldHomeSaid(ClassHome home, string name)
    {
        const string Stayed = "Turmas";
        if (TellTheOldHome(home) is not { } answer)
        {
            return (ClassFolderText.OldHomeDidNotConfirm(name), Stayed);
        }
        var (stoodDown, detail) = answer;
        if (!stoodDown)
        {
            var said = ClassFolderText.OneLine(detail);
            return (said.Length > 0 ? said : ClassFolderText.OldHomeDidNotConfirm(name), Stayed);
        }
        if (detail.Length == 0)
        {
            return (ClassFolderText.OldHadNoClasses(name), Stayed);
        }
        if (detail[0] == NotMovedMark)
        {
            var why = ClassFolderText.OneLine(detail[1..]);
            return (ClassFolderText.OldCopyNotMoved(name, why.Length > 0 ? why : ClassFolderText.UnknownReason), Stayed);
        }
        if (detail[0] == PartlyMovedMark)
        {
            var partly = ClassFolderText.OneLine(detail[1..]);
            return (ClassFolderText.OldCopyPartlyAside(name, partly.Length > 0 ? partly : null), partly.Length > 0 ? partly : null);
        }
        var folder = ClassFolderText.OneLine(detail);
        return folder.Length > 0
            ? (ClassFolderText.OldCopyAside(name, folder), folder)
            : (ClassFolderText.OldCopyAsideUnnamed(name), null);
    }

    // Where this PC's own earlier store goes: "antes de 20-10-2026 1330".
    private string BeforeWhy() => BeforeWhyStart + _localNow().ToString("dd-MM-yyyy HHmm", CultureInfo.InvariantCulture);

    // Where the old home's copy goes on it: "passadas para PROF-2" (name: as
    // the room's Agents recorded the new home; never empty).
    private static string PassedToWhy(string name)
    {
        var safe = FileNames.Safe(name.Trim());
        return PassedToWhyStart + (safe.Length > 0 && ClassFolderRules.IsAddressable(safe) ? safe : "outro PC");
    }

    // RemoteHandOverDone to the old home: what it answered (whether it stood
    // down, and its detail), or null when it gave no answer.
    private (bool StoodDown, string Detail)? TellTheOldHome(ClassHome home)
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
            channel.Send(new Message(MessageType.RemoteHandOverDone, Encoding.UTF8.GetBytes(_machineName)));
            var reply = channel.Receive(ClassFolderRemote.MaxJsonBytes);
            return reply.Type == MessageType.CommandAck ? CommandResult.Decode(reply.Payload) : null;
        }
        catch (Exception ex) when (IsNetwork(ex) || ex is OperationCanceledException)
        {
            _log($"The old class folders' home {home.Name} wasn't told of the take-over: {ex.Message}");
            return null;
        }
    }

    // What a connection to a colleague's PC throws when it drops, is refused,
    // fails its handshake, says what no Midview says, or is closed under it
    // (RemoteClassFolderController's requests go by it too).
    internal static bool IsNetwork(Exception ex) =>
        ex is IOException or SocketException or AuthenticationException or InvalidDataException or ObjectDisposedException;
}
