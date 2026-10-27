using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Agent;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class InstallClientTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly X509Certificate2 _masterCertificate = SelfSignedCertificate.Create("master-under-test");
    private readonly X509Certificate2 _agentCertificate = SelfSignedCertificate.Create("agent-under-test");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"midview-install-client-{Guid.NewGuid():N}");

    public InstallClientTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _masterCertificate.Dispose();
        _agentCertificate.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private KnownMachine Machine => new("Aluno-1", "127.0.0.1", "SESSION", _agentCertificate.Thumbprint);

    private string Installer(string name, byte[] content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public async Task TheInstaller_GoesWhole_AndTheAgentsAnswerComesBack()
    {
        var content = RandomNumberGenerator.GetBytes(FileTransfer.ChunkSize * 2 + 5);
        var path = Installer("Setup Scratch.exe", content);
        InstallRequest? asked = null;
        byte[]? received = null;
        var uploadedBeforeAnswer = false;
        var uploaded = false;
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            var request = channel.Receive();
            Assert.Equal(MessageType.InstallProgram, request.Type);
            Assert.True(ProgramInstall.TryDecodeRequest(request.Payload, out var decoded));
            asked = decoded;
            channel.Send(new Message(MessageType.InstallAccepted, Array.Empty<byte>()));
            using var file = new MemoryStream();
            while (file.Length < decoded.Size)
            {
                file.Write(channel.Receive().Payload);
            }
            received = file.ToArray();
            channel.Send(new Message(MessageType.InstallStarted, Array.Empty<byte>()));
            Thread.Sleep(200);
            uploadedBeforeAnswer = uploaded;
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, "instalado")));
        });
        var progress = new List<long>();

        var result = await Task.Run(() => InstallClient.Install(Machine, _masterCertificate, path, " /VERYSILENT ",
            (sent, _) => progress.Add(sent), () => uploaded = true, agent.Port)).WaitAsync(Timeout);
        await agent.Done.WaitAsync(Timeout);

        Assert.Equal(new InstallRequest("Setup Scratch.exe", content.Length, "/VERYSILENT"), asked);
        Assert.Equal(content, received);
        Assert.Equal(content.Length, progress[^1]);
        Assert.True(uploadedBeforeAnswer);
        Assert.Equal(new InstallResult(true, "instalado"), result);
    }

    [Fact]
    public async Task ARefusal_IsTheAgentsReason_AndNothingIsSent()
    {
        var path = Installer("setup.exe", new byte[10]);
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(false, "outra instalação já está em andamento nesta máquina")));
        });

        var result = await Task.Run(() => InstallClient.Install(Machine, _masterCertificate, path, "/S", port: agent.Port)).WaitAsync(Timeout);
        await agent.Done.WaitAsync(Timeout);

        Assert.Equal(new InstallResult(false, "outra instalação já está em andamento nesta máquina"), result);
    }

    [Fact]
    public async Task AnAgentFromBefore_IsAnOldAgent()
    {
        var path = Installer("setup.exe", new byte[10]);
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.CommandAck, new byte[] { 0 }));
        });

        var result = await Task.Run(() => InstallClient.Install(Machine, _masterCertificate, path, "/S", port: agent.Port)).WaitAsync(Timeout);
        await agent.Done.WaitAsync(Timeout);

        Assert.True(result.OldAgent);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AFileThatDidntArriveWell_IsReportedWithoutWaitingForAnInstall()
    {
        var path = Installer("setup.exe", new byte[10]);
        var uploaded = false;
        using var agent = new ScriptedAgent(_agentCertificate, _masterCertificate, channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.InstallAccepted, Array.Empty<byte>()));
            channel.Receive();
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(false, "transferência inválida")));
        });

        var result = await Task.Run(() => InstallClient.Install(Machine, _masterCertificate, path, "/S",
            uploaded: () => uploaded = true, port: agent.Port)).WaitAsync(Timeout);
        await agent.Done.WaitAsync(Timeout);

        Assert.Equal(new InstallResult(false, "transferência inválida"), result);
        Assert.False(uploaded);
    }

    // The whole way for real, on loopback: a real AgentServer, the Agent's own
    // installer and process runner, and for the installer a copy of cmd.exe
    // told to end with Windows Installer's "done, restart to finish".
    [Fact]
    public async Task OnARealAgent_TheInstallerRuns_AndItsExitCodeComesBack()
    {
        var path = Installer("setup.exe", File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "cmd.exe")));
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), "unused", "unused-hosts", new FakeFirewall());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(listener, _agentCertificate, new TrustStore(new[] { _masterCertificate.Thumbprint }), siteBlock,
            installer: new ProgramInstaller(Path.Combine(_folder, "agent"), _ => long.MaxValue, ProgramInstaller.RunProcess));
        using var stop = new CancellationTokenSource();
        var served = Task.Run(() => server.Run(stop.Token));
        try
        {
            var result = await Task.Run(() => InstallClient.Install(Machine, _masterCertificate, path, "/c exit 3010", port: port))
                .WaitAsync(TimeSpan.FromSeconds(60));

            Assert.Equal(new InstallResult(true, "instalado; reinicie a máquina para concluir"), result);
        }
        finally
        {
            stop.Cancel();
            await served.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void AnInstallerPastItsTime_IsEnded()
    {
        var started = DateTime.UtcNow;

        var exitCode = ProgramInstaller.RunProcess(
            Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 30 127.0.0.1", TimeSpan.FromSeconds(1));

        Assert.Null(exitCode);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData("JR.Inno.Setup", InstallerKind.InnoSetup)]
    [InlineData("Nullsoft.NSIS.exehead", InstallerKind.Nsis)]
    [InlineData("NullsoftInst", InstallerKind.Nsis)]
    [InlineData(".wixburn", InstallerKind.WixBurn)]
    [InlineData("nothing to see", InstallerKind.Unknown)]
    public void TheEngine_IsFoundByItsMark(string mark, InstallerKind kind)
    {
        var bytes = RandomNumberGenerator.GetBytes(4096).Concat(Encoding.ASCII.GetBytes(mark)).Concat(new byte[100]).ToArray();

        Assert.Equal(kind, InstallerDetection.Detect(Installer("setup.exe", bytes)));
    }

    [Fact]
    public void AMarkSplitBetweenTwoReads_IsStillFound()
    {
        var mark = Encoding.ASCII.GetBytes("JR.Inno.Setup");
        var bytes = new byte[(1 << 20) - 5].Concat(mark).Concat(new byte[10]).ToArray();

        Assert.Equal(InstallerKind.InnoSetup, InstallerDetection.Detect(Installer("setup.exe", bytes)));
    }

    [Fact]
    public void AnMsi_IsAnMsiWhateverItHolds()
    {
        Assert.Equal(InstallerKind.Msi, InstallerDetection.Detect(Installer("pacote.MSI", Encoding.ASCII.GetBytes("NullsoftInst"))));
    }

    private sealed class FakeFirewall : IFirewallController
    {
        public bool ApplyBlock() => true;
        public void ApplyUnblock() { }
    }

    // One connection on loopback, answered by script as an Agent would.
    private sealed class ScriptedAgent : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public ScriptedAgent(X509Certificate2 agentCertificate, X509Certificate2 masterCertificate, Action<SecureChannel> script)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Done = Task.Run(async () =>
            {
                using var client = await _listener.AcceptTcpClientAsync();
                client.ReceiveTimeout = 20_000;
                client.SendTimeout = 20_000;
                using var channel = SecureChannel.AuthenticateAsServer(
                    client, agentCertificate, new TrustStore(new[] { masterCertificate.Thumbprint }));
                script(channel);
            });
        }

        public int Port { get; }

        public Task Done { get; }

        public void Dispose() => _listener.Stop();
    }
}
