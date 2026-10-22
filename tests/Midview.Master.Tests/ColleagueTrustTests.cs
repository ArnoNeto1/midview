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
    // Well under the default bound on a whole check (5 s): an answer that didn't wait for it.
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(3);

    private readonly ColleagueTrustRoom _room;
    private readonly ConcurrentDictionary<string, bool> _offline = new() { ["PC3"] = true, ["PC4"] = true };
    private DateTime _now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    public ColleagueTrustTests(ColleagueTrustRoom room) => _room = room;

    private string Colleague => _room.Colleague.Thumbprint;

    [Fact]
    public async Task AColleagueAnOnlineAgentTrusts_IsTrusted_AndAStrangerIsNot()
    {
        var trust = NewTrust();

        Assert.True(await trust.IsTrustedAsync(Colleague).WaitAsync(Quick));
        Assert.True(await trust.IsTrustedAsync(Colleague.ToLowerInvariant()).WaitAsync(Quick));
        Assert.False(await trust.IsTrustedAsync(_room.Stranger.Thumbprint).WaitAsync(Quick));
        Assert.False(await trust.IsTrustedAsync("not a thumbprint").WaitAsync(Quick));
    }

    [Fact]
    public async Task OnlyAnAgentThatIsOff_TrustingIt_IsNotEnough()
    {
        _offline["PC1"] = true;

        Assert.False(await NewTrust().IsTrustedAsync(Colleague).WaitAsync(Quick));
    }

    [Fact]
    public async Task AYes_IsKeptTenMinutes()
    {
        var trust = NewTrust();
        Assert.True(await trust.IsTrustedAsync(Colleague).WaitAsync(Quick));
        _offline["PC1"] = true; // only PC2 left, which says no

        _now = _now.AddMinutes(10).AddSeconds(-1);
        Assert.True(await trust.IsTrustedAsync(Colleague).WaitAsync(Quick));

        _now = _now.AddSeconds(2);
        Assert.False(await trust.IsTrustedAsync(Colleague).WaitAsync(Quick));
    }

    [Fact]
    public async Task ANo_IsKeptThirtySeconds()
    {
        _offline["PC1"] = true;
        var trust = NewTrust();
        Assert.False(await trust.IsTrustedAsync(Colleague).WaitAsync(Quick));
        _offline.TryRemove("PC1", out _); // given access meanwhile, say

        _now = _now.AddSeconds(29);
        Assert.False(await trust.IsTrustedAsync(Colleague).WaitAsync(Quick));

        _now = _now.AddSeconds(2);
        Assert.True(await trust.IsTrustedAsync(Colleague).WaitAsync(Quick));
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
        Assert.False(await trust.IsTrustedAsync(Colleague).WaitAsync(Quick));

        _offline.TryRemove("PC1", out _);
        Assert.True(await trust.IsTrustedAsync(Colleague).WaitAsync(Quick));
    }

    // An Agent that never answers holds a check no longer than its bound, and
    // never holds a yes from another Agent; a check it cut short isn't kept.
    [Fact]
    public async Task AnAgentThatNeverAnswers_HoldsNoCheckPastItsBound()
    {
        _offline["PC1"] = true;
        _offline["PC2"] = true;
        _offline.TryRemove("PC4", out _);
        var trust = NewTrust(wait: TimeSpan.FromMilliseconds(500));
        try
        {
            var clock = Stopwatch.StartNew();
            Assert.False(await trust.IsTrustedAsync(Colleague).WaitAsync(Quick));
            Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(400), $"answered after {clock.Elapsed}");

            _offline.TryRemove("PC1", out _);
            Assert.True(await trust.IsTrustedAsync(Colleague).WaitAsync(Quick));
        }
        finally
        {
            _room.LetGoOfTheHanging(); // the asks still waiting on PC4 end now
        }
    }

    // The production pairing: the home's host asks the room's Agents (the rig's
    // trust the home and the other teacher).
    [Fact]
    public void TheHost_ServesTheColleaguesTheAgentsTrust()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        var trust = new ColleagueTrust(rig.Master, () => rig.Machines, _ => true, () => _now, rig.AgentPort);
        using var host = ClassFolderHost.Open(rig.Master, controller, trust.IsTrustedAsync, 0, _ => { }, IPAddress.Loopback)!;

        var colleague = Ask(host, rig.OtherMaster, rig.Master);
        var stranger = Ask(host, _room.Stranger, rig.Master);

        Assert.Equal(MessageType.RemoteClassFolderStateReply, colleague.Type);
        Assert.Equal(MessageType.CommandAck, stranger.Type);
        Assert.Equal((false, ClassFolderRemote.NotTrustedText), CommandResult.Decode(stranger.Payload));
    }

    private ColleagueTrust NewTrust(TimeSpan? wait = null) =>
        new(_room.Home, () => _room.Machines, machine => !_offline.ContainsKey(machine.Name), () => _now, _room.Port, wait);

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
