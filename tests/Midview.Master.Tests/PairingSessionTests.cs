using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PairingSessionTests : IDisposable
{
    private static readonly string Session = new('5', 40);

    private readonly X509Certificate2 _agentCert = SelfSignedCertificate.Create("agent-under-test");
    private readonly X509Certificate2 _teacher = SelfSignedCertificate.Create("teacher-under-test");

    public void Dispose()
    {
        _agentCert.Dispose();
        _teacher.Dispose();
    }

    // Work that blocks on the network (the Agent's accept and its script, the
    // teacher's side of each step) on a thread of its own: a busy test run's
    // pool can be slow to give one, past the other side's timeouts.
    private static Task OwnThread(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static Task<T> OwnThread<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // One connection to a scripted Agent on loopback.
    private (int Port, Task Done) FakeAgent(Action<SecureChannel> script)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var done = OwnThread(() =>
        {
            try
            {
                using var client = listener.AcceptTcpClient();
                client.ReceiveTimeout = 30_000;
                using var channel = SecureChannel.AuthenticateAsServerAcceptingAnyClient(client, _agentCert);
                script(channel);
            }
            finally
            {
                listener.Stop();
            }
        });
        return (port, done);
    }

    // An Agent that pairs as Pairing says; records what it saw.
    private sealed class HonestAgent
    {
        public string TeacherName { get; private set; } = string.Empty;

        public int Code { get; private set; } = -1;

        public MessageType Answer { get; private set; }

        public void Run(SecureChannel channel, string agentThumbprint)
        {
            var request = channel.Receive();
            Assert.Equal(MessageType.PairRequest, request.Type);
            TeacherName = Encoding.UTF8.GetString(request.Payload);
            var agentNonce = Pairing.NewNonce();
            channel.Send(new Message(MessageType.PairCommit, Pairing.Commit(agentNonce)));
            var masterNonce = channel.Receive().Payload;
            Code = Pairing.Code(channel.RemoteThumbprint, agentThumbprint, agentNonce, masterNonce);
            channel.Send(new Message(MessageType.PairReveal, agentNonce));
            Answer = channel.Receive().Type;
            if (Answer == MessageType.PairConfirm)
            {
                channel.Send(new Message(MessageType.PairDone, new PairingResult("ALUNO-07", Session, "00:1a:2b:3c:4d:5e").Encode()));
            }
        }
    }

    [Fact]
    public async Task Confirming_GivesTheMachineForTheClassroomList()
    {
        var agent = new HonestAgent();
        var (port, done) = FakeAgent(channel => agent.Run(channel, _agentCert.Thumbprint));

        using var session = await OwnThread(() => PairingSession.Start("127.0.0.1", _teacher, "PROF-01", port)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(agent.Code, session.Code);
        Assert.Equal(Pairing.Format(agent.Code), session.CodeText);
        Assert.Equal(_agentCert.Thumbprint, session.AgentThumbprint);

        var machine = await OwnThread(session.Confirm).WaitAsync(TimeSpan.FromSeconds(30));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("PROF-01", agent.TeacherName);
        Assert.Equal(new KnownMachine("ALUNO-07", "127.0.0.1", Session, _agentCert.Thumbprint, "00-1A-2B-3C-4D-5E"), machine);
    }

    [Fact]
    public async Task Cancelling_TellsTheMachine()
    {
        var agent = new HonestAgent();
        var (port, done) = FakeAgent(channel => agent.Run(channel, _agentCert.Thumbprint));

        using (var session = await OwnThread(() => PairingSession.Start("127.0.0.1", _teacher, "PROF-01", port)).WaitAsync(TimeSpan.FromSeconds(30)))
        {
            session.Cancel();
        }
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(MessageType.PairCancel, agent.Answer);
    }

    [Fact]
    public async Task ARevealThatBreaksTheCommitment_IsRefused()
    {
        var (port, done) = FakeAgent(channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.PairCommit, Pairing.Commit(Pairing.NewNonce())));
            channel.Receive();
            channel.Send(new Message(MessageType.PairReveal, Pairing.NewNonce()));
            try
            {
                channel.Receive();
            }
            catch (Exception)
            {
                // The teacher's side hung up, as it should.
            }
        });

        var error = await Assert.ThrowsAsync<PairingRefusedException>(() =>
            OwnThread(() => PairingSession.Start("127.0.0.1", _teacher, "PROF-01", port)).WaitAsync(TimeSpan.FromSeconds(30)));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(PairingSession.InvalidAnswer, error.Message);
    }

    [Fact]
    public async Task ARefusal_CarriesTheMachinesReason()
    {
        var (port, done) = FakeAgent(channel =>
        {
            channel.Receive();
            channel.Send(Pairing.Refused("Esta máquina já tem professor."));
        });

        var error = await Assert.ThrowsAsync<PairingRefusedException>(() =>
            OwnThread(() => PairingSession.Start("127.0.0.1", _teacher, "PROF-01", port)).WaitAsync(TimeSpan.FromSeconds(30)));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("Esta máquina já tem professor.", error.Message);
    }

    [Fact]
    public async Task AnOldAgent_IsToldToUpdate()
    {
        var (port, done) = FakeAgent(channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.CommandAck, new byte[] { 0 }));
        });

        var error = await Assert.ThrowsAsync<PairingRefusedException>(() =>
            OwnThread(() => PairingSession.Start("127.0.0.1", _teacher, "PROF-01", port)).WaitAsync(TimeSpan.FromSeconds(30)));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(PairingSession.OldAgent, error.Message);
    }
}
