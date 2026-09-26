using System.Net;
using System.Net.Sockets;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class ScheduledShutdownTests : IDisposable
{
    // Monday 28/09/2026, and a schedule for 18:00 on weekdays.
    private static readonly DateTime Monday = new(2026, 9, 28);
    private static readonly PowerSchedule Weekdays6pm = new(1000, PowerSchedule.Weekdays, 7 * 60, 18 * 60);

    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"midview-schedule-{Guid.NewGuid():N}.json");
    private readonly List<string[]> _runs = new();

    public void Dispose()
    {
        foreach (var path in new[] { _statePath, _statePath + ".corrupt" })
        {
            File.Delete(path);
        }
    }

    private ScheduledShutdown Create() => new(_statePath, args =>
    {
        _runs.Add(args);
        return 0;
    });

    [Fact]
    public void IsDue_WhenTheTimeFallsBetweenTwoChecks() =>
        Assert.True(ScheduledShutdown.IsDue(Weekdays6pm, Monday.AddHours(17.99), Monday.AddHours(18.01)));

    [Fact]
    public void IsDue_NotBefore_NorAgainAfter()
    {
        Assert.False(ScheduledShutdown.IsDue(Weekdays6pm, Monday.AddHours(17.9), Monday.AddHours(17.95)));
        Assert.False(ScheduledShutdown.IsDue(Weekdays6pm, Monday.AddHours(18.01), Monday.AddHours(18.02)));
    }

    [Fact]
    public void IsDue_NotOnADayOffTheSchedule()
    {
        var saturday = Monday.AddDays(5);
        Assert.False(ScheduledShutdown.IsDue(Weekdays6pm, saturday.AddHours(17.99), saturday.AddHours(18.01)));
    }

    [Fact]
    public void IsDue_TurnedOnAfterTheTime_DoesNotShutDownStraightAway()
    {
        // The Agent's first check compares against when it started.
        var startedAt = Monday.AddHours(19);
        Assert.False(ScheduledShutdown.IsDue(Weekdays6pm, startedAt, startedAt.AddSeconds(20)));
    }

    [Fact]
    public void IsDue_AsleepThroughTheTime_OnlyShortlyAfter()
    {
        Assert.True(ScheduledShutdown.IsDue(Weekdays6pm, Monday.AddHours(17.5), Monday.AddHours(18.25)));
        Assert.False(ScheduledShutdown.IsDue(Weekdays6pm, Monday.AddHours(17.5), Monday.AddHours(19)));
    }

    [Fact]
    public void IsDue_ACheckAcrossMidnight_StillCatchesLateEveningTimes()
    {
        var at2359 = Weekdays6pm with { ShutdownMinute = 23 * 60 + 59 };
        Assert.True(ScheduledShutdown.IsDue(at2359, Monday.AddHours(23.98), Monday.AddDays(1).AddSeconds(10)));
    }

    [Fact]
    public void IsDue_NoShutdownScheduled_Never() =>
        Assert.False(ScheduledShutdown.IsDue(
            Weekdays6pm with { ShutdownMinute = null }, Monday.AddHours(17.99), Monday.AddHours(18.01)));

    [Fact]
    public void Check_WhenDue_RunsShutdownWithTheScheduledWarning()
    {
        var scheduled = Create();
        scheduled.Handle(Weekdays6pm.Encode());

        Assert.True(scheduled.Check(Monday.AddHours(17.99), Monday.AddHours(18.01)));

        var args = Assert.Single(_runs);
        Assert.Equal(new[] { "/s", "/f", "/t", "300" }, args[..4]);
        Assert.StartsWith("Desligamento programado", args[^1]);
    }

    [Fact]
    public void Check_WithoutASchedule_NeverRunsAnything()
    {
        Assert.False(Create().Check(Monday.AddHours(17.99), Monday.AddHours(18.01)));
        Assert.Empty(_runs);
    }

    [Fact]
    public void Handle_KeepsTheSchedule_AcrossRestarts()
    {
        var (succeeded, detail) = Create().Handle(Weekdays6pm.Encode());

        Assert.True(succeeded);
        Assert.Equal("desliga sozinha às 18:00 (seg a sex)", detail);
        Assert.Equal(Weekdays6pm, Create().Current);
    }

    [Fact]
    public void Handle_AnOlderSchedule_IsKeptOut()
    {
        var scheduled = Create();
        scheduled.Handle(Weekdays6pm.Encode());

        var (succeeded, detail) = scheduled.Handle((Weekdays6pm with { SavedAt = 999, ShutdownMinute = 12 * 60 }).Encode());

        Assert.True(succeeded);
        Assert.StartsWith("mantido o agendamento mais novo", detail);
        Assert.Equal(Weekdays6pm, scheduled.Current);
    }

    [Fact]
    public void Handle_ANewerScheduleWithoutShutdown_ClearsIt()
    {
        var scheduled = Create();
        scheduled.Handle(Weekdays6pm.Encode());

        var (_, detail) = scheduled.Handle((Weekdays6pm with { SavedAt = 2000, ShutdownMinute = null }).Encode());

        Assert.Equal("sem desligamento programado", detail);
        Assert.False(scheduled.Check(Monday.AddHours(17.99), Monday.AddHours(18.01)));
    }

    [Fact]
    public void Handle_AnInvalidRequest_ChangesNothing()
    {
        var scheduled = Create();

        Assert.Equal((false, "pedido inválido"), scheduled.Handle(new byte[3]));
        Assert.Null(scheduled.Current);
    }

    [Fact]
    public void AnUnreadableFile_MeansNoSchedule_AndIsKeptAside()
    {
        File.WriteAllText(_statePath, "{ não é json");

        Assert.Null(Create().Current);
        Assert.True(File.Exists(_statePath + ".corrupt"));
    }

    [Fact]
    public async Task SetPowerSchedule_ThroughTheAgentServer_AnswersWithWhatItWillDo()
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), Path.GetTempFileName(), "unused-hosts-path", new FakeFirewallController());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(
            listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), siteBlock, scheduledShutdown: Create());
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
                channel.Send(new Message(MessageType.SetPowerSchedule, Weekdays6pm.Encode()));
                return channel.Receive();
            }).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(MessageType.CommandAck, ack.Type);
            Assert.Equal((true, "desliga sozinha às 18:00 (seg a sex)"), CommandResult.Decode(ack.Payload));
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
