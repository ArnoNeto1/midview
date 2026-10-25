using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Protocol;

namespace Midview.Master;

// What a take-over came to, in the teacher's words (the window shows Detail as it is).
public sealed record HandOverResult(bool Ok, string Detail);

// How far a copy has come: the files and bytes that arrived, of all of them.
public readonly record struct HandOverProgress(int Files, int TotalFiles, long Bytes, long TotalBytes);

// "Guardar as pastas das turmas neste computador" ("Two teacher PCs in one
// lab", docs/superpowers/specs/2026-10-20-class-folder-design.md; plan
// decision 7, Rulings 4 and 9, Review Focus 5): this teacher PC takes the class
// folders over from the PC that keeps them (the home), or, as that home, gives
// them. One per Master, for its store (ClassStore) and sync (ClassFolderSync).
//
// Taking them over from a home that answers (TakeOverAsync): the home's whole
// store comes over its ClassFolderHost into Staging, every path checked to stay
// inside it and every size against what was announced, and all of it must fit
// on this PC's disk with KeepFreeBytes to spare. Nothing changes on either PC or
// on the Agents until all of it is there and checks out; any failure before
// then throws the copy away. Then this PC's own earlier store goes aside to
// "Turmas (antes de <dd-MM-yyyy HHmm>)", the copy goes in its place, and every
// online Agent is claimed with a take-over (ClassHomeClaim, a generation newer
// than now and than any home they keep). When none of them takes it (a lesson
// on, say), everything is put back as it was. When one does, this PC's
// generation is kept (ClassHomeFileMaster), and the old home is told
// (MessageType.RemoteHandOverDone): it stands down and moves its copy aside to
// "Turmas (passadas para <this PC>)". An old home that doesn't answer that
// finds out by itself (ClassHomeTracker) and keeps its copy where it is.
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
// CommandAck: true once the old home stood down (its detail says why its copy
// couldn't go aside, if it couldn't), false when the room's Agents don't keep
// the sender as the home.
//
// In three files: this one (taking over), ClassFolderHandOver.Send.cs (giving,
// on the home) and ClassFolderHandOver.Receive.cs (the copy arriving, checked).
public sealed partial class ClassFolderHandOver
{
    // What a take-over leaves free on this PC's disk at least: Windows and the PCs' files need room too.
    public const long KeepFreeBytes = 1L << 30;

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
    // arrives, on the copy's thread. Never throws.
    public Task<HandOverResult> TakeOverAsync(ClassHome home, IProgress<HandOverProgress>? progress = null) =>
        Alone(() => TakeOver(home, progress));

    // The home PC (named home in the texts) doesn't answer: the Agents take
    // this PC as the home, and its own store (or none) is the one now. Never throws.
    public Task<HandOverResult> TakeOverUnreachableAsync(string home) => Alone(() =>
    {
        var (ok, detail, generation) = Claim();
        if (!ok)
        {
            return new HandOverResult(false, ClassFolderText.TakeOverUnreachableFailed(detail));
        }
        KeepGeneration(generation);
        _log($"This PC took the class folders over from {home}, which didn't answer, with generation {generation}.");
        return new HandOverResult(true, ClassFolderText.TookOverUnreachable);
    });

    // One take-over at a time, on its own thread (it blocks on the network and the disk).
    private Task<HandOverResult> Alone(Func<HandOverResult> takeOver)
    {
        if (Interlocked.Exchange(ref _taking, 1) == 1)
        {
            return Task.FromResult(new HandOverResult(false, ClassFolderText.TakeOverRunning));
        }
        return Task.Factory.StartNew(
            () =>
            {
                try
                {
                    return takeOver();
                }
                catch (Exception ex)
                {
                    // A mistake in the code: nothing above throws for what the network, the disk or a PC does.
                    _log($"Taking the class folders over failed: {ex}");
                    _store.DiscardStaging();
                    return new HandOverResult(false, ClassFolderText.TakeOverUnreachableFailed(ErrorText.Describe(ex)));
                }
                finally
                {
                    Volatile.Write(ref _taking, 0);
                }
            },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private HandOverResult TakeOver(ClassHome home, IProgress<HandOverProgress>? progress)
    {
        var name = home.Name.Length > 0 ? home.Name : ClassHome.UnnamedText;
        _store.DiscardStaging();
        var received = Receive(home, name, progress);
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

        var (ok, detail, generation) = Claim();
        if (!ok)
        {
            swap.Undo();
            _store.DiscardStaging();
            return new HandOverResult(false, ClassFolderText.TakeOverFailed(name, detail));
        }
        KeepGeneration(generation);
        _log($"This PC took the class folders over from {name} with generation {generation}.");

        var text = new StringBuilder(ClassFolderText.TookOver(received.Classes, received.Files));
        if (swap.OwnStoreAside is { } aside)
        {
            text.Append(' ').Append(ClassFolderText.OwnClassesAside(Path.GetFileName(aside)));
        }
        var (answered, stoodDown, why) = TellTheOldHome(home);
        text.Append(' ').Append(
            !answered || !stoodDown ? ClassFolderText.OldHomeDidNotConfirm(name)
            : why.Length > 0 ? ClassFolderText.OldCopyNotMoved(name, why)
            : ClassFolderText.OldCopyAside(name, _machineName));
        return new HandOverResult(true, text.ToString());
    }

    // Where this PC's own earlier store goes: "antes de 20-10-2026 1330".
    private string BeforeWhy() => "antes de " + _localNow().ToString("dd-MM-yyyy HHmm", CultureInfo.InvariantCulture);

    // Where the old home's copy goes on it: "passadas para PROF-2".
    private static string PassedToWhy(string name) => "passadas para " + FileNames.Safe(name);

    // Every online Agent claimed with a take-over and a generation newer than
    // now and than any home they keep (Ruling 9). Ok when at least one took it
    // (the others are taken back at this PC's next start); otherwise why not,
    // as an Agent said it (a lesson on), or that none answered.
    private (bool Ok, string Detail, long Generation) Claim()
    {
        var online = AgentsOnline();
        var homes = Homes(online);
        var generation = ClassHomeFileMaster.TakeOverGeneration(new DateTimeOffset(_localNow()).UtcTicks, homes);
        var claim = new ClassHomeClaim(_machineName, _homePort, TakeOver: true, generation);
        var answers = AskEach(online, machine => ClaimOn(machine, claim)).OfType<ClaimAnswer>().ToList();
        foreach (var refused in answers.Where(answer => !answer.Ok))
        {
            _log($"{refused.Machine.Name} didn't take this PC as the class folders' home: {refused.Detail}");
        }
        if (answers.Any(answer => answer.Ok))
        {
            return (true, string.Empty, generation);
        }
        var why = answers.Select(answer => answer.Detail).FirstOrDefault(detail => detail.Length > 0);
        return (false, why ?? ClassFolderText.NoPcTookIt, generation);
    }

    // What the machine's Agent answered the claim; null for one from before
    // two teacher PCs (it keeps no home) or one that gave no answer.
    private ClaimAnswer? ClaimOn(KnownMachine machine, ClassHomeClaim claim)
    {
        try
        {
            var (ok, detail, oldAgent) = ClassFolderClient.ClaimHome(machine, _certificate, claim, _agentPort);
            return oldAgent ? null : new ClaimAnswer(machine, ok, detail);
        }
        catch (Exception ex)
        {
            ClassLessonOnAgents.LogUnexpected(_log, ex, $"Claiming the class folders' home on {machine.Name} failed");
            return null;
        }
    }

    // The homes the machines' Agents keep (none from one that keeps none, is
    // from before two teacher PCs, or doesn't answer).
    private List<ClassHome> Homes(IReadOnlyList<KnownMachine> machines) =>
        AskEach(machines, machine =>
        {
            try
            {
                return ClassFolderClient.GetHome(machine, _certificate, _agentPort).Home;
            }
            catch (Exception ex)
            {
                ClassLessonOnAgents.LogUnexpected(_log, ex, $"Reading the class folders' home of {machine.Name} failed");
                return null;
            }
        }).OfType<ClassHome>().ToList();

    private void KeepGeneration(long generation)
    {
        try
        {
            ClassHomeFileMaster.Write(generation, _homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The Agents keep it: this PC's next start finds itself among them as the home.
            _log($"This PC's generation as the class folders' home could not be saved: {ex.Message}");
        }
    }

    // RemoteHandOverDone to the old home: whether it answered, whether it
    // stood down, and why its copy couldn't go aside (empty when it went).
    private (bool Answered, bool StoodDown, string Why) TellTheOldHome(ClassHome home)
    {
        try
        {
            using var tcp = TcpConnect.WithTimeout(home.Address, home.Port, ConnectTimeout);
            tcp.ReceiveTimeout = tcp.SendTimeout = (int)NetworkWait.TotalMilliseconds;
            using var channel = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, home.Address, _certificate);
            if (!string.Equals(channel.RemoteThumbprint, home.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return (false, false, string.Empty);
            }
            channel.Send(new Message(MessageType.RemoteHandOverDone, Encoding.UTF8.GetBytes(_machineName)));
            var reply = channel.Receive(ClassFolderRemote.MaxJsonBytes);
            if (reply.Type != MessageType.CommandAck)
            {
                return (false, false, string.Empty);
            }
            var (stoodDown, why) = CommandResult.Decode(reply.Payload);
            return (true, stoodDown, why);
        }
        catch (Exception ex) when (IsNetwork(ex) || ex is OperationCanceledException)
        {
            _log($"The old class folders' home {home.Name} wasn't told of the take-over: {ex.Message}");
            return (false, false, string.Empty);
        }
    }

    // The online machines whose Agent the classroom list knows (one whose state can't be told is off).
    private List<KnownMachine> AgentsOnline()
    {
        IReadOnlyList<KnownMachine> machines;
        try
        {
            machines = _machines();
        }
        catch (Exception ex)
        {
            _log($"Reading the classroom list failed: {ex}");
            return new List<KnownMachine>();
        }
        return machines.Where(machine => machine.AgentThumbprint is { Length: > 0 } && IsOnline(machine)).ToList();
    }

    private bool IsOnline(KnownMachine machine)
    {
        try
        {
            return _isOnline(machine);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Each machine at once, each on its own pool thread.
    private static T[] AskEach<T>(IReadOnlyList<KnownMachine> machines, Func<KnownMachine, T> ask) =>
        Task.WhenAll(machines.Select(machine => Task.Run(() => ask(machine)))).GetAwaiter().GetResult();

    private static bool IsNetwork(Exception ex) =>
        ex is IOException or SocketException or AuthenticationException or InvalidDataException or ObjectDisposedException;

    private sealed record ClaimAnswer(KnownMachine Machine, bool Ok, string Detail);
}
