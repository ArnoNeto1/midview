using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Agent;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ColleagueTrust: whether the room's Agents trust a colleague's teacher PC
// (plan decision 5, Ruling 14), asked of real AgentServers over loopback, each
// on its own address and all on one port, as in a lab (ColleagueTrustRoom):
// PC1 trusts the colleague, PC2 doesn't, PC3 is off, and PC4 takes connections
// but never answers (off unless a test says so). Every Agent trusts the home,
// which asks. Which PCs are on, and the clock the answers are kept by, are
// each test's own.
public sealed class ColleagueTrustTests : IClassFixture<ColleagueTrustRoom>
{
    // For what only has to happen, however busy the PC running the tests is.
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(20);

    // Half of Long, and of a hanging Agent's 20 s: a check that came back before it wasn't held up.
    private static readonly TimeSpan Unbounded = TimeSpan.FromSeconds(10);

    // Where a colleague's PC asks from, and another PC of the lab.
    private static readonly IPAddress From = IPAddress.Parse("192.168.0.20");
    private static readonly IPAddress Elsewhere = IPAddress.Parse("192.168.0.21");

    private readonly ColleagueTrustRoom _room;
    private readonly ConcurrentDictionary<string, bool> _offline = new() { ["PC3"] = true, ["PC4"] = true };
    private DateTime _now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    public ColleagueTrustTests(ColleagueTrustRoom room) => _room = room;

    private string Colleague => _room.Colleague.Thumbprint;

    [Fact]
    public async Task AColleagueAnOnlineAgentTrusts_IsTrusted_AndAStrangerIsNot()
    {
        var trust = NewTrust();

        Assert.True(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));
        Assert.True(await trust.IsTrustedAsync(Colleague.ToLowerInvariant(), From).WaitAsync(Long));
        Assert.False(await trust.IsTrustedAsync(_room.Stranger.Thumbprint, From).WaitAsync(Long));
        Assert.False(await trust.IsTrustedAsync("not a thumbprint", From).WaitAsync(Long));
    }

    [Fact]
    public async Task OnlyAnAgentThatIsOff_TrustingIt_IsNotEnough()
    {
        _offline["PC1"] = true;

        Assert.False(await NewTrust().IsTrustedAsync(Colleague, From).WaitAsync(Long));
    }

    [Fact]
    public async Task AYes_IsKeptTenMinutes()
    {
        var trust = NewTrust();
        Assert.True(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));
        _offline["PC1"] = true; // only PC2 left, which says no

        _now = _now.AddMinutes(10).AddSeconds(-1);
        Assert.True(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));

        _now = _now.AddSeconds(2);
        Assert.False(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));
    }

    [Fact]
    public async Task ANo_IsKeptThirtySeconds()
    {
        _offline["PC1"] = true;
        var trust = NewTrust();
        Assert.False(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));
        _offline.TryRemove("PC1", out _); // given access meanwhile, say

        _now = _now.AddSeconds(29);
        Assert.False(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));

        _now = _now.AddSeconds(2);
        Assert.True(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));
    }

    // Without any Agent's answer there is no "no" to keep: the next check asks again.
    [Fact]
    public async Task WithNoAgentOnline_ItIsNotTrusted_AndThatIsNotKept()
    {
        foreach (var machine in _room.Machines)
        {
            _offline[machine.Name] = true;
        }
        var trust = NewTrust();
        Assert.False(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));

        _offline.TryRemove("PC1", out _);
        Assert.True(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));
    }

    // An Agent that never answers holds a check no longer than its bound, and
    // a check it cut short isn't kept: once the room can answer, it does.
    [Fact]
    public async Task AnAgentThatNeverAnswers_HoldsNoCheckPastItsBound()
    {
        _offline["PC1"] = true;
        _offline["PC2"] = true;
        _offline.TryRemove("PC4", out _);
        var trust = NewTrust(wait: TimeSpan.FromMilliseconds(500));
        try
        {
            // Bounded: PC4's ask alone would wait for CommandClient's 20 s.
            var clock = Stopwatch.StartNew();
            Assert.False(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));
            Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(400), $"answered after {clock.Elapsed}");
            Assert.True(clock.Elapsed < Unbounded, $"answered after {clock.Elapsed}");

            // Not a no kept 30 s (the clock stands still here): the next rounds, PC1
            // on, say yes. (The first may still share the round that was cut short.)
            _offline.TryRemove("PC1", out _);
            await Until(async () => await trust.IsTrustedAsync(Colleague, From));
        }
        finally
        {
            _room.LetGoOfTheHanging(); // the asks still waiting on PC4 end now
        }
    }

    // ...nor does it hold up a yes from another Agent.
    [Fact]
    public async Task AnAgentThatNeverAnswers_DoesNotHoldUpAYes()
    {
        _offline.TryRemove("PC4", out _);
        var trust = NewTrust();
        try
        {
            // Held up, it would end at its bound (Long) with no answer.
            var clock = Stopwatch.StartNew();
            Assert.True(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));
            Assert.True(clock.Elapsed < Unbounded, $"answered after {clock.Elapsed}");
        }
        finally
        {
            _room.LetGoOfTheHanging();
        }
    }

    // Ruling 15: a stranger making a new certificate for every connection gets
    // one round of the room at a time from its address; another PC isn't held
    // up by it; and every certificate is still asked about, one after the other.
    [Fact]
    public async Task AStrangerRotatingCertificates_FromOneAddress_GetsOneRoundAtATime()
    {
        using var held = new ManualResetEventSlim();
        var rounds = 0;
        var trust = NewTrust(wait: Long, isOnline: Counting(() => Interlocked.Increment(ref rounds), held));

        // Each check picks its round before it first waits: one round out for the four.
        var checks = Enumerable.Range(0, 4).Select(_ => trust.IsTrustedAsync(NewThumbprint(), From)).ToList();
        Assert.Equal(1, trust.RoundsOut);
        await Until(() => Volatile.Read(ref rounds) == 1);
        Assert.All(checks, check => Assert.False(check.IsCompleted));

        var elsewhere = trust.IsTrustedAsync(NewThumbprint(), Elsewhere);
        Assert.Equal(2, trust.RoundsOut);
        await Until(() => Volatile.Read(ref rounds) == 2);

        held.Set();
        Assert.All(await Task.WhenAll(checks).WaitAsync(Long), Assert.False);
        Assert.False(await elsewhere.WaitAsync(Long));
        Assert.Equal(5, Volatile.Read(ref rounds));
    }

    // Two checks for one teacher at once, from wherever: one round, one answer for both.
    [Fact]
    public async Task ChecksForOneTeacherAtOnce_ShareOneRound()
    {
        using var held = new ManualResetEventSlim();
        var rounds = 0;
        var trust = NewTrust(wait: Long, isOnline: Counting(() => Interlocked.Increment(ref rounds), held));

        var first = trust.IsTrustedAsync(Colleague, From);
        await Until(() => Volatile.Read(ref rounds) == 1);
        var second = trust.IsTrustedAsync(Colleague.ToLowerInvariant(), Elsewhere);
        Assert.Equal(1, trust.RoundsOut); // the second took the first's round before it first waited
        held.Set();

        Assert.True(await first.WaitAsync(Long));
        Assert.True(await second.WaitAsync(Long));
        Assert.Equal(1, Volatile.Read(ref rounds));
    }

    // One round per teacher means two never race; still, a no that comes in
    // while a yes is kept and in date leaves the yes. Here the clock puts the
    // earlier yes back in date while a round that will say no is out, as a
    // yes given meanwhile would be.
    [Fact]
    public async Task ANoFromARacingRound_DoesNotReplaceAFreshYes()
    {
        using var held = new ManualResetEventSlim(initialState: true);
        var rounds = 0;
        var trust = NewTrust(wait: Long, isOnline: Counting(() => Interlocked.Increment(ref rounds), held));
        Assert.True(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));
        var yesAt = _now;

        _now = yesAt.AddMinutes(11);
        _offline["PC1"] = true; // only PC2 left, which says no
        held.Reset();
        var racing = trust.IsTrustedAsync(Colleague, From);
        await Until(() => Volatile.Read(ref rounds) == 2); // its round is out, held at PC2
        _now = yesAt.AddMinutes(1);
        held.Set();

        Assert.False(await racing.WaitAsync(Long)); // what its own round said
        Assert.True(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long)); // the yes, still kept
    }

    // The classroom list's view of one machine failing doesn't stop the others being asked.
    [Fact]
    public async Task AMachineWhoseStateCantBeTold_IsTakenForOff_AndTheOthersAreAsked()
    {
        var trust = NewTrust(isOnline: machine => machine.Name == "PC1" ? true : throw new InvalidOperationException("roster changing"));

        Assert.True(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));
    }

    // The answers kept are at most maxKept: the oldest goes first.
    [Fact]
    public async Task TheAnswersKept_AreCapped_OldestOut()
    {
        var rounds = 0;
        var trust = NewTrust(isOnline: Counting(() => Interlocked.Increment(ref rounds)), maxKept: 2);
        var (a, b, c) = (NewThumbprint(), NewThumbprint(), NewThumbprint());
        foreach (var stranger in new[] { a, b, c })
        {
            Assert.False(await trust.IsTrustedAsync(stranger, From).WaitAsync(Long));
        }
        Assert.Equal(3, Volatile.Read(ref rounds));

        Assert.False(await trust.IsTrustedAsync(c, From).WaitAsync(Long)); // kept
        Assert.Equal(3, Volatile.Read(ref rounds));
        Assert.False(await trust.IsTrustedAsync(a, From).WaitAsync(Long)); // the oldest, gone
        Assert.Equal(4, Volatile.Read(ref rounds));
    }

    // ...a no going before any yes, however old: strangers asking can't push
    // a colleague's yes out (its PC would ask the room again every poll).
    [Fact]
    public async Task TheAnswersKept_AtTheCap_LoseANoBeforeAYes()
    {
        var rounds = 0;
        var trust = NewTrust(isOnline: Counting(() => Interlocked.Increment(ref rounds)), maxKept: 2);
        var (a, b) = (NewThumbprint(), NewThumbprint());
        Assert.True(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long));
        Assert.False(await trust.IsTrustedAsync(a, From).WaitAsync(Long));
        Assert.False(await trust.IsTrustedAsync(b, From).WaitAsync(Long));
        Assert.Equal(3, Volatile.Read(ref rounds));

        Assert.True(await trust.IsTrustedAsync(Colleague, From).WaitAsync(Long)); // kept
        Assert.Equal(3, Volatile.Read(ref rounds));
        Assert.False(await trust.IsTrustedAsync(a, From).WaitAsync(Long)); // the no, gone
        Assert.Equal(4, Volatile.Read(ref rounds));
    }

    // The production pairing: the home's host asks the room's Agents (the rig's
    // trust the home and the other teacher).
    [Fact]
    public void TheHost_ServesTheColleaguesTheAgentsTrust()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        var trust = new ColleagueTrust(rig.Master, () => rig.Machines, _ => true, () => _now, rig.AgentPort, Long);
        using var host = ClassFolderHost.Open(rig.Master, controller, trust.IsTrustedAsync, 0, _ => { }, IPAddress.Loopback)!;

        var colleague = Ask(host, rig.OtherMaster, rig.Master);
        var stranger = Ask(host, _room.Stranger, rig.Master);

        Assert.Equal(MessageType.RemoteClassFolderStateReply, colleague.Type);
        Assert.Equal(MessageType.CommandAck, stranger.Type);
        Assert.Equal((false, ClassFolderRemote.NotTrustedText), CommandResult.Decode(stranger.Payload));
    }

    // A long bound on a whole check unless a test is about the bound: on a busy
    // PC a round of the room can take more than the default 5 s.
    private ColleagueTrust NewTrust(TimeSpan? wait = null, Func<KnownMachine, bool>? isOnline = null, int maxKept = ColleagueTrust.DefaultMaxKept) =>
        new(_room.Home, () => _room.Machines, isOnline ?? IsOnline, () => _now, _room.Port, wait ?? Long, maxKept);

    private bool IsOnline(KnownMachine machine) => !_offline.ContainsKey(machine.Name);

    // The rounds a trust asks the room: each asks every machine whether it is on
    // once, PC2 among them. held: PC2's question waits for it, holding the round.
    private Func<KnownMachine, bool> Counting(Action counted, ManualResetEventSlim? held = null) => machine =>
    {
        if (machine.Name == "PC2")
        {
            counted();
            held?.Wait(Long);
        }
        return IsOnline(machine);
    };

    // A teacher no Agent knows: another certificate the same stranger made.
    private static string NewThumbprint() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));

    private static Task Until(Func<bool> condition) => Until(() => Task.FromResult(condition()));

    private static async Task Until(Func<Task<bool>> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(clock.Elapsed < Long, "waited too long");
            await Task.Delay(20);
        }
    }

    private static Message Ask(ClassFolderHost host, X509Certificate2 client, X509Certificate2 home)
    {
        using var tcp = new TcpClient();
        tcp.Connect(IPAddress.Loopback, host.Port);
        tcp.ReceiveTimeout = 30_000;
        using var channel = SecureChannel.AuthenticateAsClient(tcp, "localhost", client, new TrustStore(new[] { home.Thumbprint }));
        channel.Send(new Message(MessageType.RemoteClassFolderState, Array.Empty<byte>()));
        return channel.Receive();
    }
}

// The room ColleagueTrustTests ask, made once for all of them (certificates
// take a while to make): the teachers and PC1 to PC4.
public sealed class ColleagueTrustRoom : IDisposable
{
    private readonly List<Action> _cleanup = new();
    private readonly ConcurrentBag<TcpClient> _held = new();

    public ColleagueTrustRoom()
    {
        var listeners = OnOnePort(IPAddress.Parse("127.0.0.2"), IPAddress.Parse("127.0.0.3"), IPAddress.Parse("127.0.0.5"));
        Port = ((IPEndPoint)listeners[0].LocalEndpoint).Port;
        Machines = new[]
        {
            Agent("PC1", listeners[0], Home.Thumbprint, Colleague.Thumbprint),
            Agent("PC2", listeners[1], Home.Thumbprint),
            new KnownMachine("PC3", "127.0.0.4", "unused-session", Stranger.Thumbprint),
            Hanging("PC4", listeners[2]),
        };
    }

    public X509Certificate2 Home { get; } = SelfSignedCertificate.Create("home-under-test");

    public X509Certificate2 Colleague { get; } = SelfSignedCertificate.Create("colleague-under-test");

    public X509Certificate2 Stranger { get; } = SelfSignedCertificate.Create("stranger-under-test");

    public IReadOnlyList<KnownMachine> Machines { get; }

    public int Port { get; }

    // Closes what PC4 is holding, so the asks waiting on it end.
    public void LetGoOfTheHanging()
    {
        while (_held.TryTake(out var client))
        {
            client.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var cleanup in _cleanup)
        {
            cleanup();
        }
        LetGoOfTheHanging();
        Home.Dispose();
        Colleague.Dispose();
        Stranger.Dispose();
    }

    // A real Agent on that listener, trusting those teachers.
    private KnownMachine Agent(string name, TcpListener listener, params string[] trusted)
    {
        var certificate = SelfSignedCertificate.Create("agent-under-test");
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), "unused", "unused-hosts", new NoFirewall());
        var server = new AgentServer(listener, certificate, new TrustStore(trusted), siteBlock);
        var stop = new CancellationTokenSource();
        var run = Task.Run(() => server.Run(stop.Token));
        _cleanup.Add(() =>
        {
            stop.Cancel();
            listener.Stop();
            try
            {
                run.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Stopping the listener under the server.
            }
            certificate.Dispose();
        });
        var address = ((IPEndPoint)listener.LocalEndpoint).Address.ToString();
        return new KnownMachine(name, address, "unused-session", certificate.Thumbprint);
    }

    // A PC whose Agent takes the connection and never says anything.
    private KnownMachine Hanging(string name, TcpListener listener)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    _held.Add(await listener.AcceptTcpClientAsync());
                }
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                // Stopped.
            }
        });
        _cleanup.Add(listener.Stop);
        var address = ((IPEndPoint)listener.LocalEndpoint).Address.ToString();
        return new KnownMachine(name, address, "unused-session", Stranger.Thumbprint);
    }

    // Started listeners, one per address, all on one port.
    private static List<TcpListener> OnOnePort(params IPAddress[] addresses)
    {
        for (var attempt = 0; ; attempt++)
        {
            var listeners = new List<TcpListener>();
            try
            {
                var first = new TcpListener(addresses[0], 0);
                first.Start();
                listeners.Add(first);
                var port = ((IPEndPoint)first.LocalEndpoint).Port;
                foreach (var address in addresses.Skip(1))
                {
                    var listener = new TcpListener(address, port);
                    listener.Start();
                    listeners.Add(listener);
                }
                return listeners;
            }
            catch (SocketException) when (attempt < 20)
            {
                listeners.ForEach(listener => listener.Stop());
            }
        }
    }

    private sealed class NoFirewall : IFirewallController
    {
        public bool ApplyBlock() => true;

        public void ApplyUnblock()
        {
        }
    }
}
