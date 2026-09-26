using System.Net;
using System.Net.Sockets;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class PowerCommandHandlerTests
{
    private readonly List<string[]> _runs = new();

    private PowerCommandHandler Handler(int exitCode = 0) => new(args =>
    {
        _runs.Add(args);
        return exitCode;
    });

    [Fact]
    public void Shutdown_WithAWarning_ForcesClosedAfterTheCountdown_AndTellsTheStudentWhy()
    {
        var args = ShutdownCommands.Build(PowerAction.Shutdown, 60);

        Assert.Equal(new[] { "/s", "/f", "/t", "60", "/d", "p:0:0", "/c" }, args[..7]);
        Assert.Equal("O professor vai desligar este computador em 1 minuto. Salve seu trabalho agora: o que não estiver salvo será perdido.", args[7]);
    }

    [Fact]
    public void RestartNow_HasNoWarningToShow()
    {
        Assert.Equal(new[] { "/r", "/f", "/t", "0", "/d", "p:0:0" }, ShutdownCommands.Build(PowerAction.Restart, 0));
    }

    [Fact]
    public void Cancel_Aborts()
    {
        Assert.Equal(new[] { "/a" }, ShutdownCommands.Build(PowerAction.Cancel, 0));
    }

    [Theory]
    [InlineData(PowerAction.Shutdown, 300, 0, true, "desliga em 5 minutos")]
    [InlineData(PowerAction.Restart, 60, 0, true, "reinicia em 1 minuto")]
    [InlineData(PowerAction.Shutdown, 0, 0, true, "desligando agora")]
    [InlineData(PowerAction.Restart, 0, 0, true, "reiniciando agora")]
    [InlineData(PowerAction.Cancel, 0, 0, true, "desligamento cancelado")]
    [InlineData(PowerAction.Cancel, 0, 1116, true, "nenhum desligamento estava agendado")]
    [InlineData(PowerAction.Shutdown, 60, 1190, false, "já há um desligamento agendado (cancele antes de pedir outro)")]
    [InlineData(PowerAction.Shutdown, 60, 5, false, "o Windows recusou (código 5)")]
    public void Handle_SaysWhatHappened(PowerAction action, int delaySeconds, int exitCode, bool succeeded, string detail)
    {
        Assert.Equal((succeeded, detail), Handler(exitCode).Handle(PowerCommand.Encode(action, delaySeconds)));
        Assert.Single(_runs);
    }

    [Fact]
    public void Handle_InvalidRequest_RunsNothing()
    {
        Assert.Equal((false, "pedido inválido"), Handler().Handle(new byte[] { 7 }));
        Assert.Empty(_runs);
    }

    [Fact]
    public void Handle_ShutdownExeMissing_IsReported()
    {
        var handler = new PowerCommandHandler(_ => throw new System.ComponentModel.Win32Exception("arquivo não encontrado"));

        var (succeeded, detail) = handler.Handle(PowerCommand.Encode(PowerAction.Shutdown, 60));

        Assert.False(succeeded);
        Assert.Contains("arquivo não encontrado", detail);
    }

    [Fact]
    public void ADelayedShutdown_IsPendingUntilItsTimePlusAMargin_OrCancelled()
    {
        var now = new DateTime(2026, 9, 26, 21, 0, 0, DateTimeKind.Utc);
        var handler = new PowerCommandHandler(_ => 0, () => now);
        Assert.False(handler.IsShutdownPending());

        handler.Handle(PowerCommand.Encode(PowerAction.Shutdown, 300));
        Assert.True(handler.IsShutdownPending());

        now = now.AddMinutes(8);
        Assert.False(handler.IsShutdownPending());

        handler.Handle(PowerCommand.Encode(PowerAction.Restart, 60));
        handler.Handle(PowerCommand.Encode(PowerAction.Cancel, 0));
        Assert.False(handler.IsShutdownPending());
    }

    [Fact]
    public void ARefusedShutdown_IsNotPending()
    {
        var handler = new PowerCommandHandler(_ => 1190);

        handler.Handle(PowerCommand.Encode(PowerAction.Shutdown, 60));

        Assert.False(handler.IsShutdownPending());
    }

    [Fact]
    public async Task PowerCommand_ThroughTheAgentServer_AnswersWithWhatHappened()
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), Path.GetTempFileName(), "unused-hosts-path", new FakeFirewallController());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), siteBlock, Handler());
        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));
        try
        {
            var ack = await Task.Run(() =>
            {
                using var tcpClient = new TcpClient();
                tcpClient.Connect(IPAddress.Loopback, port);
                using var channel = SecureChannel.AuthenticateAsClient(
                    tcpClient, "agent-under-test", masterCert, new TrustStore(new[] { agentCert.Thumbprint }));
                channel.Send(new Message(MessageType.Power, PowerCommand.Encode(PowerAction.Shutdown, 60)));
                return channel.Receive();
            }).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(MessageType.CommandAck, ack.Type);
            Assert.Equal((true, "desliga em 1 minuto"), CommandResult.Decode(ack.Payload));
            Assert.Equal("/s", Assert.Single(_runs)[0]);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
