using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class AddressCheckTests
{
    private static readonly string X = new('A', 40);
    private static readonly string Y = new('B', 40);
    private static readonly string Session = new('5', 40);

    private static KnownMachine Machine(string name, string host, string? agent) => new(name, host, Session, agent);

    // --- Which tiles the Master's own search looks for ---

    [Fact]
    public void AnOfflineMachine_MayHaveMoved()
    {
        Assert.True(AddressCheck.MayHaveMoved(Machine("Aluno-2", "10.0.0.2", X), TilePresence.Offline, "Offline"));
        Assert.True(AddressCheck.MayHaveMoved(Machine("Aluno-2", "10.0.0.2", X), TilePresence.Offline, "Sem resposta"));
    }

    // Its old address now answers as something else: another student's PC
    // (its Session doesn't know this Master) or a device with nothing listening.
    [Fact]
    public void AnAddressThatAnswersAsAnotherDevice_MayHaveMoved()
    {
        Assert.True(AddressCheck.MayHaveMoved(Machine("Aluno-2", "10.0.0.2", X), TilePresence.Online, "Certificado recusado"));
        Assert.True(AddressCheck.MayHaveMoved(Machine("Aluno-2", "10.0.0.2", X), TilePresence.Online, "Sem usuário logado"));
    }

    [Fact]
    public void AMachineShowingItsScreen_OrStillConnecting_IsNotLookedFor()
    {
        Assert.False(AddressCheck.MayHaveMoved(Machine("Aluno-2", "10.0.0.2", X), TilePresence.Online, string.Empty));
        Assert.False(AddressCheck.MayHaveMoved(Machine("Aluno-2", "10.0.0.2", X), TilePresence.Online, "Tela indisponível"));
        Assert.False(AddressCheck.MayHaveMoved(Machine("Aluno-2", "10.0.0.2", X), TilePresence.Connecting, "Conectando..."));
    }

    [Fact]
    public void AMachineASearchCantMove_IsNotLookedFor()
    {
        Assert.False(AddressCheck.MayHaveMoved(Machine("Aluno-2", "ALUNO-02", X), TilePresence.Offline, "Offline"));
        Assert.False(AddressCheck.MayHaveMoved(Machine("Aluno-2", "10.0.0.2", null), TilePresence.Offline, "Offline"));
    }

    // --- The proof before moving ---

    // After the router hands out addresses again, Aluno-05 and Aluno-06 swap
    // theirs: each tile reaches the other PC ("Certificado recusado"), both are
    // looked for, both answer the search from the other address, and both move.
    [Fact]
    public async Task TwoPcsThatSwappedAddresses_BothMove()
    {
        var roster = new[] { Machine("Aluno-05", "10.0.0.5", X), Machine("Aluno-06", "10.0.0.6", Y) };
        var found = new[]
        {
            new FoundMachine("Aluno-05", "10.0.0.6", X, "you", false),
            new FoundMachine("Aluno-06", "10.0.0.5", Y, "you", false),
        };
        var answers = new HashSet<(string, string)> { ("10.0.0.6", X), ("10.0.0.5", Y) };

        var changes = Roster.NewAddressesWhere(roster, found,
            index => AddressCheck.MayHaveMoved(roster[index], TilePresence.Online, "Certificado recusado"));
        var proven = await AddressCheck.Proven(
            changes.Select(change => (roster[change.Index], change.Updated)).ToList(),
            (address, agent) => answers.Contains((address, agent)));

        Assert.Equal(new[] { ("Aluno-05", "10.0.0.6"), ("Aluno-06", "10.0.0.5") },
            proven.Select(move => (move.Updated.Name, move.Updated.HostAddress)).ToArray());
    }

    // A student's program answers the search in a PC's name from its own
    // address: nothing there proves the PC's Agent, so the PC stays.
    [Fact]
    public async Task AnAnswerTheAgentDoesntBackUp_MovesNothing()
    {
        var old = Machine("Aluno-02", "10.0.0.2", X);
        var moves = new[] { (old, old with { HostAddress = "10.0.0.66" }) };

        Assert.Empty(await AddressCheck.Proven(moves, (_, _) => false));
    }

    // A PC with two network cards answered from the other one: its Agent is
    // still at the address in the list, so it stays put.
    [Fact]
    public async Task AMachineStillAnsweringAtItsAddress_StaysPut()
    {
        var old = Machine("Aluno-02", "10.0.0.2", X);
        var moves = new[] { (old, old with { HostAddress = "192.168.1.2" }) };

        Assert.Empty(await AddressCheck.Proven(moves, (_, agent) => agent == X));
    }

    [Fact]
    public async Task TheMachinesOwnAgent_AtTheNewAddressOnly_Moves()
    {
        var old = Machine("Celular 35F7", "192.168.15.13", X);
        var moves = new[] { (old, old with { HostAddress = "192.168.0.134" }) };

        var proven = await AddressCheck.Proven(moves, (address, agent) => address == "192.168.0.134" && agent == X);

        Assert.Equal("192.168.0.134", proven.Single().Updated.HostAddress);
    }

    // --- The handshake itself, against a real TLS listener ---

    [Fact]
    public void TheAgentWithThePinnedCertificate_Answers()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        using var agent = SelfSignedCertificate.Create("agent-under-test");
        using var listener = new FakeAgent(agent, master);

        Assert.True(AddressCheck.AgentAnswers("127.0.0.1", agent.Thumbprint, master, listener.Port, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void AnotherCertificate_DoesNotAnswerForTheAgent()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        using var agent = SelfSignedCertificate.Create("agent-under-test");
        using var impostor = SelfSignedCertificate.Create("impostor");
        using var listener = new FakeAgent(impostor, master);

        Assert.False(AddressCheck.AgentAnswers("127.0.0.1", agent.Thumbprint, master, listener.Port, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void NothingListening_DoesNotAnswer()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        var unused = new TcpListener(IPAddress.Loopback, 0);
        unused.Start();
        var port = ((IPEndPoint)unused.LocalEndpoint).Port;
        unused.Stop();

        Assert.False(AddressCheck.AgentAnswers("127.0.0.1", X, master, port, TimeSpan.FromSeconds(5)));
    }

    // Takes connections and does the server's side of the handshake, as an
    // Agent does, then hangs up.
    private sealed class FakeAgent : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public FakeAgent(X509Certificate2 certificate, X509Certificate2 master)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            new Thread(() =>
            {
                try
                {
                    using var client = _listener.AcceptTcpClient();
                    client.ReceiveTimeout = 10_000;
                    using var channel = SecureChannel.AuthenticateAsServer(client, certificate, new TrustStore(new[] { master.Thumbprint }));
                }
                catch (Exception)
                {
                    // The client gave up on this certificate, or the test ended.
                }
            }) { IsBackground = true }.Start();
        }

        public int Port { get; }

        public void Dispose() => _listener.Stop();
    }
}
