using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PhoneClaimerTests : IDisposable
{
    private static readonly string Session = new('7', 40);

    private readonly X509Certificate2 _agentCert = SelfSignedCertificate.Create("phone-under-test");
    private readonly X509Certificate2 _teacher = SelfSignedCertificate.Create("teacher-under-test");

    public void Dispose()
    {
        _agentCert.Dispose();
        _teacher.Dispose();
    }

    private static Task<T> OwnThread<T>(Func<T> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static Task OwnThread(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

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

    // A phone that knows the token: answers with the proof over the Master's nonce.
    private static Message Done(SecureChannel channel, string agentThumbprint, string masterThumbprint, string token, PhoneClaimRequest request, Func<string, string>? tamper = null)
    {
        var proof = PhoneClaim.Proof(token, request.Nonce, agentThumbprint, masterThumbprint);
        return new Message(MessageType.PairDone,
            new PairingResult("Celular 07", Session, null, DeviceKindText.AndroidWire, tamper is null ? proof : tamper(proof)).Encode());
    }

    [Fact]
    public async Task Claim_sends_nonce_never_the_token_and_accepts_the_proof()
    {
        PhoneClaimRequest? seen = null;
        byte[] raw = Array.Empty<byte>();
        var (port, done) = FakeAgent(channel =>
        {
            var message = channel.Receive();
            Assert.Equal(MessageType.ClaimPhone, message.Type);
            raw = message.Payload;
            seen = PhoneClaimRequest.Decode(message.Payload);
            channel.Send(Done(channel, _agentCert.Thumbprint, _teacher.Thumbprint, "tok-abc", seen!));
        });

        var machine = await OwnThread(() => PhoneClaimer.Claim("127.0.0.1", _teacher, "PROF-01", "tok-abc", port)).WaitAsync(TimeSpan.FromSeconds(30));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("PROF-01", seen!.TeacherName);
        Assert.DoesNotContain("tok-abc", Encoding.UTF8.GetString(raw));
        Assert.Equal("Celular 07", machine.Name);
        Assert.True(machine.IsPhone);
        Assert.Equal(_agentCert.Thumbprint, machine.AgentThumbprint);
        Assert.Equal(Session, machine.SessionThumbprint);
    }

    [Fact]
    public async Task Claim_with_a_wrong_proof_is_refused()
    {
        var (port, done) = FakeAgent(channel =>
        {
            var request = PhoneClaimRequest.Decode(channel.Receive().Payload)!;
            channel.Send(Done(channel, _agentCert.Thumbprint, _teacher.Thumbprint, "outro-token", request));
        });

        var ex = await Assert.ThrowsAsync<PairingRefusedException>(() =>
            OwnThread(() => PhoneClaimer.Claim("127.0.0.1", _teacher, "PROF-01", "tok-abc", port)).WaitAsync(TimeSpan.FromSeconds(30)));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(PairingSession.InvalidAnswer, ex.Message);
    }

    [Fact]
    public async Task Claim_without_a_proof_is_refused()
    {
        var (port, done) = FakeAgent(channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.PairDone, new PairingResult("Celular 07", Session, null, DeviceKindText.AndroidWire).Encode()));
        });

        var ex = await Assert.ThrowsAsync<PairingRefusedException>(() =>
            OwnThread(() => PhoneClaimer.Claim("127.0.0.1", _teacher, "PROF-01", "tok-abc", port)).WaitAsync(TimeSpan.FromSeconds(30)));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(PairingSession.InvalidAnswer, ex.Message);
    }

    [Fact]
    public async Task Claim_refused_carries_the_text()
    {
        var (port, done) = FakeAgent(channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.PairRefused, Encoding.UTF8.GetBytes("Este celular não espera este computador.")));
        });

        var ex = await Assert.ThrowsAsync<PairingRefusedException>(() =>
            OwnThread(() => PhoneClaimer.Claim("127.0.0.1", _teacher, "PROF-01", "tok", port)).WaitAsync(TimeSpan.FromSeconds(30)));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("Este celular não espera este computador.", ex.Message);
    }

    [Fact]
    public async Task Claim_answered_by_CommandAck_is_an_old_agent()
    {
        var (port, done) = FakeAgent(channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.CommandAck, Array.Empty<byte>()));
        });

        var ex = await Assert.ThrowsAsync<PairingRefusedException>(() =>
            OwnThread(() => PhoneClaimer.Claim("127.0.0.1", _teacher, "PROF-01", "tok", port)).WaitAsync(TimeSpan.FromSeconds(30)));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(PairingSession.OldAgent, ex.Message);
    }
}
