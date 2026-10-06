using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// The Agent over real TLS on loopback. Any certificate gets through the
// handshake now, so what keeps a stranger out is AgentServer's gate.
public class AgentPairingTests : IDisposable
{
    private readonly string _trustedPath = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid():N}.json");
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"midview-state-{Guid.NewGuid():N}.json");
    private readonly X509Certificate2 _agentCert = SelfSignedCertificate.Create("agent-under-test");
    private readonly X509Certificate2 _teacher = SelfSignedCertificate.Create("teacher-under-test");
    private readonly FakeFirewallController _firewall = new();
    private readonly RecordingDisplay _display = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serverTask;
    private readonly int _port;

    public AgentPairingTests()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var trust = TrustStore.FromFile(_trustedPath);
        var pairing = new PairingService(trust, _trustedPath, _display, () => DateTime.Now, "ALUNO-07", () => new string('5', 40));
        var handler = new SiteBlockCommandHandler(new SiteBlockState(), _statePath, "unused-hosts-path", _firewall);
        var server = new AgentServer(listener, _agentCert, trust, handler, pairing: pairing);
        _serverTask = Task.Run(() => server.Run(_stop.Token));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _serverTask.Wait(TimeSpan.FromSeconds(5));
        File.Delete(_trustedPath);
        File.Delete(_statePath);
        _agentCert.Dispose();
        _teacher.Dispose();
    }

    private (TcpClient Tcp, SecureChannel Channel) Connect()
    {
        var tcp = new TcpClient();
        tcp.Connect(IPAddress.Loopback, _port);
        tcp.ReceiveTimeout = 30_000;
        return (tcp, SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, "agent-under-test", _teacher));
    }

    // The teacher's side of Pairing, by hand (the Master's PairingSession
    // does the same).
    private (int Code, PairingResult Result) PairOverTheNetwork()
    {
        var (tcp, channel) = Connect();
        using var _ = tcp;
        using var __ = channel;
        channel.Send(new Message(MessageType.PairRequest, Encoding.UTF8.GetBytes("PROF-01")));
        var commit = channel.Receive();
        Assert.Equal(MessageType.PairCommit, commit.Type);
        var masterNonce = Pairing.NewNonce();
        channel.Send(new Message(MessageType.PairNonce, masterNonce));
        var reveal = channel.Receive();
        Assert.Equal(MessageType.PairReveal, reveal.Type);
        Assert.True(Pairing.Opens(commit.Payload, reveal.Payload));
        Assert.Equal(_agentCert.Thumbprint, channel.RemoteThumbprint);
        var code = Pairing.Code(_teacher.Thumbprint, channel.RemoteThumbprint, reveal.Payload, masterNonce);
        channel.Send(new Message(MessageType.PairConfirm, Array.Empty<byte>()));
        var done = channel.Receive();
        Assert.Equal(MessageType.PairDone, done.Type);
        return (code, PairingResult.Decode(done.Payload)!);
    }

    // The reply, or null when the Agent closed the connection unanswered.
    private Message? Command(MessageType type)
    {
        var (tcp, channel) = Connect();
        using var _ = tcp;
        using var __ = channel;
        channel.Send(new Message(type, Array.Empty<byte>()));
        try
        {
            return channel.Receive();
        }
        catch (Exception)
        {
            return null;
        }
    }

    [Fact]
    public async Task APairedTeacher_IsTrustedForCommands()
    {
        Assert.Null(await Task.Run(() => Command(MessageType.UnblockInternet)).WaitAsync(TimeSpan.FromSeconds(30)));

        var (code, result) = await Task.Run(PairOverTheNetwork).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(code, _display.Shown.Single());
        Assert.Equal("ALUNO-07", result.Name);

        var ack = await Task.Run(() => Command(MessageType.UnblockInternet)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(MessageType.CommandAck, ack!.Type);
    }

    [Theory]
    [InlineData(MessageType.Power)]
    [InlineData(MessageType.SetPowerSchedule)]
    [InlineData(MessageType.UpdateOffer)]
    [InlineData(MessageType.GetUpdateStatus)]
    [InlineData(MessageType.SetAppBlock)]
    [InlineData(MessageType.GetAppBlock)]
    [InlineData(MessageType.GetWakeAddress)]
    [InlineData(MessageType.BlockInternet)]
    [InlineData(MessageType.UnblockInternet)]
    [InlineData(MessageType.BlockDomain)]
    [InlineData(MessageType.UnblockDomain)]
    [InlineData(MessageType.AllowNewTeacher)]
    public async Task ATeacherItDoesNotTrust_IsCutOffUnanswered(MessageType command)
    {
        File.WriteAllText(_trustedPath, $"[\"{new string('C', 40)}\"]");

        var reply = await Task.Run(() => Command(command)).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Null(reply);
        Assert.Equal(0, _firewall.ApplyBlockCalls);
        Assert.Equal(0, _firewall.ApplyUnblockCalls);
    }

    [Fact]
    public async Task AllowNewTeacher_FromATrustedTeacher_OpensTheWindow()
    {
        File.WriteAllText(_trustedPath, $"[\"{_teacher.Thumbprint}\"]");

        var ack = await Task.Run(() => Command(MessageType.AllowNewTeacher)).WaitAsync(TimeSpan.FromSeconds(30));

        var (succeeded, detail) = CommandResult.Decode(ack!.Payload);
        Assert.True(succeeded);
        Assert.StartsWith("liberada até", detail);
    }

    private sealed class RecordingDisplay : IPairingCodeDisplay
    {
        public bool SomeoneLoggedOn => true;

        public List<int> Shown { get; } = new();

        public IDisposable? Show(int code, string teacherName)
        {
            Shown.Add(code);
            return new MemoryStream();
        }
    }
}
