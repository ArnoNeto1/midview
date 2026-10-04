using System.Net;
using System.Net.Sockets;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class AppBlockCommandHandlerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-appblock-{Guid.NewGuid():N}.json");
    private readonly List<AppBlockPolicy> _applied = new();

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + ".tmp", _path + ".corrupt" })
        {
            File.Delete(file);
        }
    }

    private AppBlockCommandHandler Create() => new(_path, _applied.Add);

    private static byte[] Encoded(long savedAt, AppBlockMode mode, params string[] programs) =>
        new AppBlockPolicy(savedAt, mode, programs).Encode();

    [Fact]
    public void ABlockList_IsAppliedAndDescribed()
    {
        var handler = Create();

        var (succeeded, detail) = handler.Handle(Encoded(10, AppBlockMode.BlockList, "chrome.exe", "roblox.exe"));

        Assert.True(succeeded);
        Assert.Equal("bloqueando 2 programa(s)", detail);
        Assert.Equal(AppBlockMode.BlockList, Assert.Single(_applied).Mode);
        Assert.Equal(AppBlockMode.BlockList, handler.Current.Mode);
    }

    [Fact]
    public void AllowOnly_IsDescribed()
    {
        Assert.Equal("permitindo só o Windows e 1 programa(s)",
            Create().Handle(Encoded(10, AppBlockMode.AllowOnly, "winword.exe")).Detail);
        Assert.Equal("permitindo só o Windows",
            Create().Handle(Encoded(20, AppBlockMode.AllowOnly)).Detail);
    }

    [Fact]
    public void TheBlock_SurvivesARestart()
    {
        Create().Handle(Encoded(10, AppBlockMode.BlockList, "chrome.exe"));

        var restarted = Create();

        Assert.Equal(AppBlockMode.BlockList, restarted.Current.Mode);
        Assert.Equal(new[] { "chrome.exe" }, restarted.Current.Programs);
        Assert.Equal(10, restarted.Current.SavedAt);
    }

    [Fact]
    public void AnOlderPolicy_IsKeptOut_AndSaysSo()
    {
        var handler = Create();
        handler.Handle(Encoded(2000, AppBlockMode.BlockList, "chrome.exe"));

        var (succeeded, detail) = handler.Handle(Encoded(1000, AppBlockMode.AllowOnly, "winword.exe"));

        Assert.True(succeeded);
        Assert.StartsWith("mantido o bloqueio mais novo, salvo em", detail);
        Assert.Equal(AppBlockMode.BlockList, handler.Current.Mode);
        Assert.Single(_applied);
    }

    [Fact]
    public void ANewerLift_Lifts_AndAnOlderBlockResentLaterDoesNotUndoIt()
    {
        var handler = Create();
        handler.Handle(Encoded(1000, AppBlockMode.BlockList, "chrome.exe"));
        handler.Handle(AppBlockPolicy.Lifted(2000).Encode());
        Assert.False(handler.Current.IsActive);

        // The poller of a Master that didn't see the lift yet sends the old block again.
        var (_, detail) = handler.Handle(Encoded(1000, AppBlockMode.BlockList, "chrome.exe"));

        Assert.StartsWith("mantido o bloqueio mais novo", detail);
        Assert.False(handler.Current.IsActive);
    }

    [Fact]
    public void TheSameSavedAt_SameRules_IsIdempotent_DifferentRules_IsRefused()
    {
        var handler = Create();
        handler.Handle(Encoded(1000, AppBlockMode.BlockList, "a.exe", "b.exe"));

        Assert.True(handler.Handle(Encoded(1000, AppBlockMode.BlockList, "b.exe", "a.exe")).Succeeded);
        Assert.Single(_applied);

        var (succeeded, detail) = handler.Handle(Encoded(1000, AppBlockMode.BlockList, "c.exe"));
        Assert.False(succeeded);
        Assert.Contains("aplique de novo", detail);
    }

    [Fact]
    public void AnInvalidPayload_IsRefused()
    {
        var handler = Create();

        Assert.Equal((false, "pedido inválido"), handler.Handle(new byte[] { 1, 2, 3 }));
        Assert.Empty(_applied);
    }

    [Fact]
    public void ACorruptFile_BlocksNothing_AndIsKeptAside()
    {
        File.WriteAllText(_path, "{ not json");

        var handler = Create();

        Assert.False(handler.Current.IsActive);
        Assert.True(File.Exists(_path + ".corrupt"));
    }

    [Fact]
    public void AFileWithAProtectedName_IsTreatedAsCorrupt()
    {
        File.WriteAllText(_path, """{"SavedAt":5,"Mode":1,"Programs":["explorer.exe"]}""");

        Assert.False(Create().Current.IsActive);
        Assert.True(File.Exists(_path + ".corrupt"));
    }

    [Fact]
    public async Task ThroughTheAgentServer_SetAndGetAppBlock_WorkAndTheOldAnswerIsRecognizable()
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), Path.GetTempFileName(), "unused-hosts-path", new FakeFirewallController());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(
            listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), siteBlock, appBlock: Create());
        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));
        try
        {
            Message Ask(MessageType type, byte[] payload)
            {
                using var tcpClient = new TcpClient();
                tcpClient.Connect(IPAddress.Loopback, port);
                using var channel = SecureChannel.AuthenticateAsClient(
                    tcpClient, "agent-under-test", masterCert, new TrustStore(new[] { agentCert.Thumbprint }));
                channel.Send(new Message(type, payload));
                return channel.Receive();
            }

            var (ack, status) = await Task.Run(() => (
                Ask(MessageType.SetAppBlock, Encoded(10, AppBlockMode.BlockList, "chrome.exe")),
                Ask(MessageType.GetAppBlock, Array.Empty<byte>()))).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal((true, "bloqueando 1 programa(s)"), CommandResult.Decode(ack.Payload));
            Assert.Equal(MessageType.AppBlockStatus, status.Type);
            Assert.True(AppBlockPolicy.TryDecode(status.Payload, out var policy));
            Assert.Equal(new[] { "chrome.exe" }, policy.Programs);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task AnAgentWithoutTheHandler_AnswersAnAckWithoutDetail_LikeAnOlderOne()
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), Path.GetTempFileName(), "unused-hosts-path", new FakeFirewallController());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), siteBlock);
        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));
        try
        {
            var reply = await Task.Run(() =>
            {
                using var tcpClient = new TcpClient();
                tcpClient.Connect(IPAddress.Loopback, port);
                using var channel = SecureChannel.AuthenticateAsClient(
                    tcpClient, "agent-under-test", masterCert, new TrustStore(new[] { agentCert.Thumbprint }));
                channel.Send(new Message(MessageType.GetAppBlock, Array.Empty<byte>()));
                return channel.Receive();
            }).WaitAsync(TimeSpan.FromSeconds(30));

            // What an Agent from before this feature does with a command it
            // doesn't know: the Master reads it as an old Agent.
            Assert.Equal(MessageType.CommandAck, reply.Type);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void ASaveThatFails_KeepsThePreviousPolicy()
    {
        var handler = Create();
        handler.Handle(Encoded(1000, AppBlockMode.BlockList, "chrome.exe"));
        Directory.CreateDirectory(_path + ".tmp"); // the temp file can't be created

        var (succeeded, detail) = handler.Handle(Encoded(2000, AppBlockMode.AllowOnly, "winword.exe"));

        Assert.False(succeeded);
        Assert.StartsWith("não foi possível gravar o bloqueio", detail);
        Assert.Equal(AppBlockMode.BlockList, handler.Current.Mode);
        Assert.Single(_applied);
        Directory.Delete(_path + ".tmp");
    }
}
