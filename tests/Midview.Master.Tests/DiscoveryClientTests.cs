using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class DiscoveryClientTests
{
    private static readonly string Master = new('A', 40);
    private static readonly string X = new('B', 40);
    private static readonly string Y = new('C', 40);

    [Fact]
    public void Merge_OneEntryPerMachine_InClassroomOrder_OnlyForThisProbe()
    {
        var probe = Guid.NewGuid();
        var answers = new (IPAddress, DiscoveryReply)[]
        {
            (IPAddress.Parse("10.0.0.10"), new DiscoveryReply(probe, "Aluno-10", X, TeacherStatus.None, false)),
            (IPAddress.Parse("192.168.56.10"), new DiscoveryReply(probe, "Aluno-10", X.ToLowerInvariant(), TeacherStatus.None, false)),
            (IPAddress.Parse("10.0.0.2"), new DiscoveryReply(probe, "Aluno-2", Y, TeacherStatus.You, false)),
            (IPAddress.Parse("10.0.0.3"), new DiscoveryReply(Guid.NewGuid(), "Aluno-3", new string('D', 40), TeacherStatus.None, false)),
        };

        var found = DiscoveryClient.Merge(probe, answers);

        Assert.Equal(
            new[]
            {
                new FoundMachine("Aluno-2", "10.0.0.2", Y, TeacherStatus.You, false),
                new FoundMachine("Aluno-10", "10.0.0.10", X, TeacherStatus.None, false),
            },
            found);
    }

    [Fact]
    public async Task Search_FindsAMachineThatAnswers()
    {
        using var agentSide = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)agentSide.Client.LocalEndPoint!).Port;
        var answering = Task.Run(async () =>
        {
            var received = await agentSide.ReceiveAsync();
            var probe = Discovery.ParseProbe(received.Buffer)!;
            Assert.Equal(Master, probe.MasterThumbprint);
            var reply = Discovery.Encode(new DiscoveryReply(probe.Id, "ALUNO-07", X, TeacherStatus.None, false));
            await agentSide.SendAsync(reply, reply.Length, received.RemoteEndPoint);
        });

        var found = await Task.Run(() =>
            DiscoveryClient.Search(Master, new[] { new IPEndPoint(IPAddress.Loopback, port) }, TimeSpan.FromSeconds(2)));

        await answering.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { new FoundMachine("ALUNO-07", "127.0.0.1", X, TeacherStatus.None, false) }, found);
    }
}
