using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// Whether this Master works the class folders itself or through another
// teacher PC ("Pasta da turma" with two teacher PCs in one lab).
public enum ClassFolderMode
{
    // This PC keeps the class folders, or no PC does yet (this one becomes the
    // home when it starts a lesson).
    Local,

    // Another teacher PC keeps them (ClassHomeTracker.Home).
    Remote,
}

// What one refresh of ClassHomeTracker found: the home and the mode, as Home
// and Mode were then, and how many online Agents it asked (none: no machine
// was on, so it found nothing out, whatever it says). FileRead: whether this
// PC's class-home.json could be read for it (a missing one can); when not,
// neither this PC's generation nor the last home it saw counted, so a Local
// naming this PC may come from stragglers alone.
// StartedAt: when the refresh began asking (Environment.TickCount64), so a
// reading that says what the room said before something changed is told apart.
public sealed record ClassHomeReading(ClassHome? Home, ClassFolderMode Mode, int Asked, bool FileRead = true, long StartedAt = 0);

// Which teacher PC keeps the class folders (plan decision 8): what
// the room's online Agents say (MessageType.GetClassHome), asked every
// interval (30 s by default) and whenever RefreshAsync is called (the window
// opening). An Agent from before two teacher PCs, one that doesn't answer, or
// a machine whose Agent identity the classroom list doesn't have, says
// nothing, and so does one that keeps no home. The home is the one reported
// with the newest generation (ClassHome.Generation), however few Agents have
// heard of it yet, as a start goes by it (ClassFolderSync); between PCs of the
// same generation, the name that sorts last, as a start decides it (Outranks);
// between records of one PC, the one most Agents report, and on a tie the one
// already known. No home reported is no home.
//
// This PC is the home when it is the home's PC as the Agents themselves decide
// (ClassHome.IsSamePc): its certificate and computer name, its certificate and
// the generation it keeps on file (ClassHomeFileMaster: the PC renamed), or its
// name under another certificate (Midview reinstalled, or paired again).
//
// Another PC seen as the home is kept on file (ClassHomeFileMaster), and with
// no home reported, a PC that was never the home but saw one goes by it:
// Remote, as its starts are refused for it (no silent cancel). So does a PC
// whose own generation is older than the one seen, whatever Agents naming it
// say (stragglers).
//
// machines and isOnline are called from background threads, and Refreshed is
// raised on them. Nothing an Agent does escapes as an exception.
public sealed class ClassHomeTracker : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);

    // How long Dispose waits for a refresh in progress.
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(5);

    private readonly X509Certificate2 _masterCertificate;
    private readonly string _machineName;
    private readonly Func<IReadOnlyList<KnownMachine>> _machines;
    private readonly Func<KnownMachine, bool> _isOnline;
    private readonly TimeSpan _interval;
    private readonly int _agentPort;
    private readonly string _homePath;

    // One refresh at a time, so the last one to finish is the newest.
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private State _state = new(null, ClassFolderMode.Local);
    private int _disposed;

    // machineName: this PC's computer name (Environment.MachineName), what its
    // claims carry. homePath: its generation as the home (ClassHomeFileMaster.
    // Path). Until the first refresh: no home, Local.
    public ClassHomeTracker(
        X509Certificate2 masterCertificate, string machineName, Func<IReadOnlyList<KnownMachine>> machines,
        Func<KnownMachine, bool> isOnline, TimeSpan? interval = null, int agentPort = ConnectionClient.AgentPort,
        string? homePath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(machineName);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        _masterCertificate = masterCertificate;
        _machineName = machineName;
        _machines = machines;
        _isOnline = isOnline;
        _interval = interval ?? DefaultInterval;
        _agentPort = agentPort;
        _homePath = homePath ?? ClassHomeFileMaster.Path;
        _loop = Task.Run(LoopAsync);
    }

    // Raised (on a background thread) after every refresh, changed or not,
    // once Home and Mode are what it found and before RefreshAsync returns:
    // what it found, so a change can be taken only once two readings in a row
    // say it (ClassFolderRole).
    public event Action<ClassHomeReading>? Refreshed;

    // The PC that keeps the class folders, as the room says; null for none.
    public ClassHome? Home => Volatile.Read(ref _state).Home;

    public ClassFolderMode Mode => Volatile.Read(ref _state).Mode;

    // Asks every online Agent at once and goes by what they answer. Refreshed
    // is raised once the refresh is over, so a handler may refresh again.
    public async Task RefreshAsync()
    {
        ClassHomeReading reading;
        await _refreshing.WaitAsync().ConfigureAwait(false);
        try
        {
            var startedAt = Environment.TickCount64;
            // In the order they arrive.
            var answers = new ConcurrentQueue<ClassHome?>();
            var online = ClassLessonOnAgents.Online(_machines, _isOnline, Log);
            await Task.WhenAll(online.Select(machine => Task.Run(() => Ask(machine, answers)))).ConfigureAwait(false);

            // This PC's generation and the last other home it saw; neither when
            // the file can't be read just now (only no file means none).
            var (fileRead, generation, seen) = ReadFile();

            var home = Choose(answers.ToList(), Home);
            if (home is null && fileRead && generation is null)
            {
                // Never the home, but it saw another PC as the home.
                home = seen;
            }
            else if (fileRead && seen is not null && IsNewerThanThisPc(seen, home, generation))
            {
                // Another home seen newer than this PC is (one that
                // took the room back after this PC gave a take-over up, say),
                // even with only Agents naming this PC, or none, answering.
                home = seen;
            }
            var state = new State(
                home, home is not null && !IsThisPc(home, generation) ? ClassFolderMode.Remote : ClassFolderMode.Local);
            Volatile.Write(ref _state, state);
            if (state.Mode == ClassFolderMode.Remote && fileRead)
            {
                RememberOtherHome(home!);
            }
            reading = new ClassHomeReading(state.Home, state.Mode, online.Count, fileRead, startedAt);
        }
        finally
        {
            _refreshing.Release();
        }

        RaiseRefreshed(reading);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        try
        {
            _loop.Wait(DisposeWait);
        }
        catch (AggregateException)
        {
            // The loop catches everything; nothing to report here.
        }
    }

    // Whether home keeps the room over the PC with that generation and
    // computer name: a newer generation, or the same one and a name that
    // sorts after it (ignoring case). Every teacher PC decides it alike: a
    // start between two PCs claiming at once (ClassFolderSync.Contest.cs), and
    // the tracker between two PCs of one generation (Choose).
    internal static bool Outranks(ClassHome home, long generation, string name) =>
        home.Generation > generation
        || (home.Generation == generation && string.Compare(home.Name, name, StringComparison.OrdinalIgnoreCase) > 0);

    // The home that outranks every other one of homes (Outranks); null for none.
    internal static ClassHome? Strongest(IEnumerable<ClassHome> homes) =>
        homes.Aggregate((ClassHome?)null, (best, home) => best is null || Outranks(home, best.Generation, best.Name) ? home : best);

    // The home that outranks the others the answers name (none: no home);
    // between records of that PC (another certificate, say), the one most
    // answers name, then current (the home known so far), then the one of the
    // newest answer. It is told as its newest answer has it (the address a
    // claim refreshed last, most likely). answers: in the order they arrived.
    internal static ClassHome? Choose(IReadOnlyList<ClassHome?> answers, ClassHome? current)
    {
        var homes = answers.OfType<ClassHome>().Select((home, order) => (Home: home, Order: order)).ToList();
        if (Strongest(homes.Select(answer => answer.Home)) is not { } strongest)
        {
            return null;
        }

        // Its own records: none of them outranked by it (its generation and name).
        var groups = homes.Where(answer => !Outranks(strongest, answer.Home.Generation, answer.Home.Name))
            .GroupBy(answer => KeyOf(answer.Home)).ToList();
        var most = groups.Max(group => group.Count());
        var tied = groups.Where(group => group.Count() == most).ToList();
        var winner = tied.Count == 1
            ? tied[0]
            : tied.FirstOrDefault(group => current is not null && group.Key == KeyOf(current))
              ?? tied.MaxBy(group => group.Max(answer => answer.Order))!;
        return winner.MaxBy(answer => answer.Order).Home;
    }

    // One PC as its Agents record it.
    private static string KeyOf(ClassHome home) =>
        $"{home.Thumbprint.ToUpperInvariant()}\n{home.Name.ToUpperInvariant()}\n{home.Generation}";

    private bool IsThisPc(ClassHome home, long? generation) =>
        home.IsSamePc(_masterCertificate.Thumbprint, _machineName, generation);

    // Whether another PC seen as the home is newer than this PC: the home
    // chosen is this PC, or none, and seen's generation is above both this
    // PC's own (generation, on file) and the one the Agents give it.
    private bool IsNewerThanThisPc(ClassHome seen, ClassHome? home, long? generation)
    {
        if (IsThisPc(seen, generation) || (home is not null && !IsThisPc(home, generation)))
        {
            return false;
        }
        return seen.Generation > Math.Max(generation ?? 0, home?.Generation ?? 0);
    }

    private (bool Read, long? Generation, ClassHome? Seen) ReadFile()
    {
        try
        {
            return (true, ClassHomeFileMaster.Read(_homePath), ClassHomeFileMaster.ReadLastOtherHome(_homePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"What this PC knows of the class folders' home could not be read: {ex.Message}");
            return (false, null, null);
        }
    }

    // A PC that saw another one as the home doesn't become the home
    // by starting a lesson (ClassFolderSync), only by taking the folders over.
    private void RememberOtherHome(ClassHome home)
    {
        try
        {
            ClassHomeFileMaster.WriteLastOtherHome(home, _homePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"The class folders' home seen could not be saved: {ex.Message}");
        }
    }

    private void Ask(KnownMachine machine, ConcurrentQueue<ClassHome?> answers)
    {
        try
        {
            var (home, oldAgent) = ClassFolderClient.GetHome(machine, _masterCertificate, _agentPort);
            if (!oldAgent)
            {
                answers.Enqueue(home);
            }
        }
        catch (Exception ex)
        {
            // Off, unreachable or not trusting this teacher: no answer.
            if (ex is not (SocketException or IOException or AuthenticationException or OperationCanceledException))
            {
                Log($"{machine.Name} gave no usable answer about the class folders' home: {ex.Message}");
            }
        }
    }

    private async Task LoopAsync()
    {
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, token).ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Asking for the class folders' home failed: {ex}");
            }
        }
    }

    private void RaiseRefreshed(ClassHomeReading reading)
    {
        try
        {
            Refreshed?.Invoke(reading);
        }
        catch (Exception ex)
        {
            Log($"A Refreshed handler failed: {ex}");
        }
    }

    private static void Log(string message) => System.Diagnostics.Trace.WriteLine("ClassHomeTracker: " + message);

    private sealed record State(ClassHome? Home, ClassFolderMode Mode);
}
