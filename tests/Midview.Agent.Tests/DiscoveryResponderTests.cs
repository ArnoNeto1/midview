using System.Net;
using System.Net.Sockets;
using System.Text;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class DiscoveryResponderTests : IDisposable
{
    private static readonly string Teacher = new('A', 40);
    private static readonly string Other = new('C', 40);
    private static readonly string AgentThumbprint = new('B', 40);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    private PairingService Service() =>
        new(TrustStore.FromFile(_path), _path, new NoDisplay(), () => new DateTime(2026, 10, 5, 14, 0, 0), "ALUNO-07", () => new string('5', 40));

    private static DiscoveryReply Ask(DiscoveryResponder responder, string master, Guid probe) =>
        Discovery.ParseReply(responder.Answer(Discovery.Encode(new DiscoveryProbe(probe, master)))!)!;

    [Fact]
    public void AMachineWithNoTeacher_SaysSo()
    {
        var probe = Guid.NewGuid();

        var reply = Ask(new DiscoveryResponder(Service(), AgentThumbprint, "ALUNO-07"), Teacher, probe);

        Assert.Equal(new DiscoveryReply(probe, "ALUNO-07", AgentThumbprint, TeacherStatus.None, false), reply);
    }

    [Fact]
    public void ItTellsTheAskingTeacherWhetherItIsTheirs()
    {
        File.WriteAllText(_path, $"[\"{Teacher}\"]");
        var responder = new DiscoveryResponder(Service(), AgentThumbprint, "ALUNO-07");

        Assert.Equal(TeacherStatus.You, Ask(responder, Teacher, Guid.NewGuid()).Teachers);
        Assert.Equal(TeacherStatus.Others, Ask(responder, Other, Guid.NewGuid()).Teachers);
    }

    [Fact]
    public void ItSaysWhenANewTeacherMayPair()
    {
        File.WriteAllText(_path, $"[\"{Teacher}\"]");
        var service = Service();
        service.AllowNewTeacher();

        Assert.True(Ask(new DiscoveryResponder(service, AgentThumbprint, "ALUNO-07"), Other, Guid.NewGuid()).NewTeacherOpen);
    }

    [Fact]
    public void WhatIsNotAProbe_GetsNoAnswer()
    {
        var responder = new DiscoveryResponder(Service(), AgentThumbprint, "ALUNO-07");

        Assert.Null(responder.Answer(Encoding.UTF8.GetBytes("olá")));
        Assert.Null(responder.Answer(Discovery.Encode(new DiscoveryReply(Guid.NewGuid(), "X", AgentThumbprint, TeacherStatus.None, false))));
    }

    [Fact]
    public async Task ItAnswersAProbeOverUdp()
    {
        using var agentSide = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)agentSide.Client.LocalEndPoint!).Port;
        using var stop = new CancellationTokenSource();
        var running = new DiscoveryResponder(Service(), AgentThumbprint, "ALUNO-07").RunAsync(agentSide, stop.Token);
        try
        {
            using var master = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var probe = Guid.NewGuid();
            var packet = Discovery.Encode(new DiscoveryProbe(probe, Teacher));
            await master.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, port));

            var received = await master.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(probe, Discovery.ParseReply(received.Buffer)!.ProbeId);
        }
        finally
        {
            stop.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class NoDisplay : IPairingCodeDisplay
    {
        public bool SomeoneLoggedOn => true;

        public IDisposable? Show(int code, string teacherName) => null;
    }
}
