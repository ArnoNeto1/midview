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

// Which teacher PC keeps the class folders (plan decision 8): what the room's
// online Agents say (MessageType.GetClassHome), asked every interval (30 s by
// default) and whenever RefreshAsync is called (the window opening). An Agent
// from before two teacher PCs, one that doesn't answer, or a machine whose
// Agent identity the classroom list doesn't have, says nothing. The
// home is the one most of the others report, an Agent with no home being a
// vote for none, as it is when a start claims the home (ClassFolderSync); a tie
// goes to the one of the newest answer. No answer at all is no home.
//
// This PC is the home when the home has its certificate and computer name, or
// its name under another certificate (Midview reinstalled, or paired again),
// as the Agents themselves decide (ClassHomeState).
//
// machines and isOnline are called from background threads, and Changed is
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

    // One refresh at a time, so the last one to finish is the newest.
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private State _state = new(null, ClassFolderMode.Local);
    private int _disposed;

    // machineName: this PC's computer name (Environment.MachineName), what its
    // claims carry. Until the first refresh: no home, Local.
    public ClassHomeTracker(
        X509Certificate2 masterCertificate, string machineName, Func<IReadOnlyList<KnownMachine>> machines,
        Func<KnownMachine, bool> isOnline, TimeSpan? interval = null, int agentPort = ConnectionClient.AgentPort)
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
        _loop = Task.Run(LoopAsync);
    }

    // Raised (on a background thread) when the home or the mode changes.
    public event Action? Changed;

    // The PC that keeps the class folders, as the room says; null for none.
    public ClassHome? Home => Volatile.Read(ref _state).Home;

    public ClassFolderMode Mode => Volatile.Read(ref _state).Mode;

    // Asks every online Agent at once and goes by what they answer.
    public async Task RefreshAsync()
    {
        await _refreshing.WaitAsync().ConfigureAwait(false);
        try
        {
            // In the order they arrive.
            var answers = new ConcurrentQueue<ClassHome?>();
            var online = Machines().Where(machine => machine.AgentThumbprint is { Length: > 0 } && IsOnline(machine)).ToList();
            await Task.WhenAll(online.Select(machine => Task.Run(() => Ask(machine, answers)))).ConfigureAwait(false);

            var home = Choose(answers.ToList());
            var state = new State(home, home is not null && !IsThisPc(home) ? ClassFolderMode.Remote : ClassFolderMode.Local);
            var before = Interlocked.Exchange(ref _state, state);
            if (before != state)
            {
                RaiseChanged();
            }
        }
        finally
        {
            _refreshing.Release();
        }
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

    // The home most answers name (an answer of none counts too); a tie goes
    // to the group with the newest answer, and the home is told as its newest
    // answer has it (the address a claim refreshed last, most likely).
    // answers: in the order they arrived.
    internal static ClassHome? Choose(IReadOnlyList<ClassHome?> answers)
    {
        if (answers.Count == 0)
        {
            return null;
        }

        var winner = answers
            .Select((home, order) => (Home: home, Order: order))
            .GroupBy(answer => answer.Home is null ? null : IdentityOf(answer.Home))
            .MaxBy(group => (group.Count(), group.Max(answer => answer.Order)))!;
        return winner.MaxBy(answer => answer.Order).Home;
    }

    private static string? IdentityOf(ClassHome home) =>
        home.Thumbprint.ToUpperInvariant() + "\n" + home.Name.ToUpperInvariant();

    private bool IsThisPc(ClassHome home) =>
        home.IsSameIdentity(_masterCertificate.Thumbprint, _machineName)
        || (home.Name.Length > 0 && string.Equals(home.Name, _machineName, StringComparison.OrdinalIgnoreCase));

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

    private IReadOnlyList<KnownMachine> Machines()
    {
        try
        {
            return _machines();
        }
        catch (Exception ex)
        {
            Log($"The list of machines could not be read: {ex.Message}");
            return Array.Empty<KnownMachine>();
        }
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

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log($"A Changed handler failed: {ex}");
        }
    }

    private static void Log(string message) => System.Diagnostics.Trace.WriteLine("ClassHomeTracker: " + message);

    private sealed record State(ClassHome? Home, ClassFolderMode Mode);
}
