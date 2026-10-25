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

// How far a copy has come: the files and bytes that arrived, of all of them.
// InPlace: the copy is whole and in place, and the room's PCs are being told;
// from here on the take-over can't be cancelled.
public readonly record struct HandOverProgress(int Files, int TotalFiles, long Bytes, long TotalBytes, bool InPlace = false);

// "Guardar as pastas das turmas neste computador" ("Two teacher PCs in one
// lab", docs/superpowers/specs/2026-10-20-class-folder-design.md; plan
// decision 7, Rulings 4, 9 and 17, Review Focus 5): this teacher PC takes the
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
// this PC goes on with its own store, or none.
//
// Giving them, on the home (for ClassFolderHost): Send streams the store
// (ClassFolderHandOver.Send.cs) and HandedOverAsync stands down once the room's
// Agents keep the sender as the home. Never while a lesson is on.
//
// On the wire (ClassFolderRemote.Port, after the host's checks), RemoteHandOver
// is answered with one CommandAck refusing it (CommandResult false, why), or
// with the store: first ManifestPath (JSON: every class, and every file that
// follows, in order, with its size and last write time in UTC ticks), then each
// of those files as a SendFile header (FileTransfer.EncodeHeader: the size, then
// the path: "files/<turma>/<path>", "index/<turma>.json" or "lessons.json") and
// FileChunks adding up to exactly that size, and last one CommandAck (true,
// "<n> arquivos"). A CommandAck in the middle says why the rest won't come.
// RemoteHandOverDone (the new home's computer name, UTF-8) is answered with one
// CommandAck: true once the old home stood down, its detail the folder its copy
// went to (empty: it had none; NotMovedMark and why: it couldn't go aside);
// false, with why in the teacher's words, when it doesn't stand down (the
// room's Agents don't keep the sender as the home, or a lesson is on there).
//
// In four files: this one (taking over), ClassFolderHandOver.Claim.cs (the
// Agents), ClassFolderHandOver.Send.cs (giving, on the home) and
// ClassFolderHandOver.Receive.cs (the copy arriving, checked).
public sealed partial class ClassFolderHandOver
{
    // What a take-over leaves free on this PC's disk at least: Windows and the PCs' files need room too.
    public const long KeepFreeBytes = 1L << 30;

    // Starts RemoteHandOverDone's detail when the old home's copy couldn't go
    // aside (never in a folder name); ErrorText's words follow.
    internal const char NotMovedMark = '|';

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
    internal TimeSpan NetworkWait { get; set; } = TimeSpan.FromMinutes(2);

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
    // nothing, until it goes in place; ignored after. Never throws.
    public Task<HandOverResult> TakeOverAsync(
        ClassHome home, IProgress<HandOverProgress>? progress = null, CancellationToken cancel = default)
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
            var (ok, detail, generation) = Claim();
            if (!ok)
            {
                return new HandOverResult(false, ClassFolderText.TakeOverUnreachableFailed(detail));
            }
            stage.Value = Stage.Claimed;
            KeepGeneration(generation);
            _log($"This PC took the class folders over from {home}, which didn't answer, with generation {generation}.");
            return new HandOverResult(true, ClassFolderText.TookOverUnreachable);
        },
        (stage, reason) => stage.Value == Stage.Copying
            ? new HandOverResult(false, ClassFolderText.TakeOverUnreachableFailed(reason))
            : new HandOverResult(stage.Value == Stage.Claimed, ClassFolderText.TakeOverUnreachableBrokeOff(reason)));

    // One take-over at a time, on its own thread (it blocks on the network and
    // the disk). brokeOff: what to say when it throws (a mistake in the code:
    // nothing in it throws for what the network, the disk or a PC does), by the
    // stage it got to; a copy not in place yet is thrown away.
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
                try
                {
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
                    Volatile.Write(ref _taking, 0);
                }
            },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private HandOverResult TakeOver(
        ClassHome home, string name, IProgress<HandOverProgress>? progress, CancellationToken cancel, StrongBox<Stage> stage)
    {
        _store.DiscardStaging();
        if (_store.StagingLeft() is { } left)
        {
            return new HandOverResult(false, ClassFolderText.TakeOverFailed(name, ClassFolderText.StagingLeft(Path.GetFileName(left))));
        }

        var received = Receive(home, name, progress, cancel);
        if (received.Failure is null && cancel.IsCancellationRequested)
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
            return new HandOverResult(false, ClassFolderText.TakeOverFailed(name, ClassFolderText.CopyNotSaved(ErrorText.Describe(ex))));
        }

        stage.Value = Stage.Claiming;
        progress?.Report(new HandOverProgress(received.Files, received.Files, 0, 0, InPlace: true));
        var (ok, detail, generation) = Claim();
        if (!ok)
        {
            var putBack = swap.Undo();
            _store.DiscardStaging();
            return new HandOverResult(false, putBack
                ? ClassFolderText.TakeOverFailed(name, detail)
                : ClassFolderText.TakeOverNotPutBack(
                    name, detail, swap.OwnStoreAside is { } ownAside ? Path.GetFileName(ownAside) : null));
        }
        stage.Value = Stage.Claimed;
        KeepGeneration(generation);
        _log($"This PC took the class folders over from {name} with generation {generation}.");

        var text = new StringBuilder(ClassFolderText.TookOver(received.Classes, received.Files));
        if (swap.OwnStoreAside is { } aside)
        {
            text.Append(' ').Append(ClassFolderText.OwnClassesAside(Path.GetFileName(aside)));
        }
        return new HandOverResult(true, text.Append(' ').Append(WhatTheOldHomeSaid(home, name)).ToString());
    }

    // RemoteHandOverDone to the old home, and its answer in the teacher's
    // words; what that PC said goes on one short line, whatever it was.
    private string WhatTheOldHomeSaid(ClassHome home, string name)
    {
        if (TellTheOldHome(home) is not { } answer)
        {
            return ClassFolderText.OldHomeDidNotConfirm(name);
        }
        var (stoodDown, detail) = answer;
        var said = ClassFolderText.OneLine(detail.Length > 0 && detail[0] == NotMovedMark ? detail[1..] : detail);
        return !stoodDown ? said.Length > 0 ? said : ClassFolderText.OldHomeDidNotConfirm(name)
            : said.Length == 0 ? ClassFolderText.OldHadNoClasses(name)
            : detail[0] == NotMovedMark ? ClassFolderText.OldCopyNotMoved(name, said)
            : ClassFolderText.OldCopyAside(name, said);
    }

    // Where this PC's own earlier store goes: "antes de 20-10-2026 1330".
    private string BeforeWhy() => "antes de " + _localNow().ToString("dd-MM-yyyy HHmm", CultureInfo.InvariantCulture);

    // Where the old home's copy goes on it: "passadas para PROF-2" (name: as
    // the room's Agents recorded the new home; never empty).
    private static string PassedToWhy(string name)
    {
        var safe = FileNames.Safe(name.Trim());
        return "passadas para " + (safe.Length > 0 && ClassFolderRules.IsAddressable(safe) ? safe : "outro PC");
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

    private static bool IsNetwork(Exception ex) =>
        ex is IOException or SocketException or AuthenticationException or InvalidDataException or ObjectDisposedException;
}
