using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Drives PhoneNetworkClient against a scripted stand-in for a phone's agent
// (never a real phone): one that knows "Liberar Wi-Fi e modo avião", and an
// older one.
public sealed class PhoneNetworkClientTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private static Task<T> Run<T>(Func<T> call) =>
        Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(Limit);

    private static Message Ack(bool succeeded, string detail) => new(MessageType.CommandAck, CommandResult.Encode(succeeded, detail));

    [Fact]
    public async Task Free_sends_103_with_the_minutes()
    {
        using var phone = new ScriptedPhone(Ack(true, "Wi-Fi e modo avião liberados por 30 minutos"));

        var answer = await Run(() => PhoneNetworkClient.Free(phone.Machine, 30, phone.MasterCertificate, phone.Port));

        Assert.Equal(MessageType.FreePhoneNetwork, phone.Request!.Type);
        Assert.Equal(new byte[] { 30 }, phone.Request.Payload);
        Assert.Equal((true, "Wi-Fi e modo avião liberados por 30 minutos"), answer);
    }

    [Fact]
    public async Task Lock_sends_103_with_zero()
    {
        using var phone = new ScriptedPhone(Ack(true, "Wi-Fi e modo avião travados"));

        await Run(() => PhoneNetworkClient.Free(phone.Machine, PhoneNetworkFree.LockNow, phone.MasterCertificate, phone.Port));

        Assert.Equal(new byte[] { 0 }, phone.Request!.Payload);
    }

    // Only the hash goes out: the payload, never the password.
    [Fact]
    public async Task The_password_goes_as_its_hash()
    {
        using var phone = new ScriptedPhone(Ack(true, "senha de administrador posta no celular"));
        var payload = PhoneAdminPassword.Encode("abc123");

        var answer = await Run(() => PhoneNetworkClient.SetAdminPassword(phone.Machine, payload, phone.MasterCertificate, phone.Port));

        Assert.Equal(MessageType.SetPhoneAdminPassword, phone.Request!.Type);
        Assert.Equal(payload, phone.Request.Payload);
        Assert.True(PhoneAdminPassword.Verify("abc123", PhoneAdminPassword.Decode(phone.Request.Payload)!));
        Assert.Equal((true, "senha de administrador posta no celular"), answer);
    }

    [Fact]
    public async Task Removing_the_password_sends_nothing()
    {
        using var phone = new ScriptedPhone(Ack(true, "senha de administrador tirada do celular"));

        await Run(() => PhoneNetworkClient.SetAdminPassword(phone.Machine, Array.Empty<byte>(), phone.MasterCertificate, phone.Port));

        Assert.Empty(phone.Request!.Payload);
    }

    [Theory]
    [InlineData("Ainda não funciona em celular.")]
    [InlineData("")]
    public async Task An_older_phone_is_null(string detail)
    {
        using var phone = new ScriptedPhone(Ack(false, detail));

        var answer = await Run(() => PhoneNetworkClient.Free(phone.Machine, 10, phone.MasterCertificate, phone.Port));

        Assert.Null(answer);
        Assert.Equal(PhoneNetworkText.OldAgent, PhoneNetworkText.Line(answer));
    }

    [Fact]
    public async Task A_refusal_is_shown_as_the_agent_said_it()
    {
        using var phone = new ScriptedPhone(Ack(false, "Este celular não foi preparado pelo Midview."));

        var answer = await Run(() => PhoneNetworkClient.Free(phone.Machine, 10, phone.MasterCertificate, phone.Port));

        Assert.Equal("Este celular não foi preparado pelo Midview.", PhoneNetworkText.Line(answer));
    }

    [Fact]
    public void Bad_input_is_refused_before_connecting()
    {
        var phone = new KnownMachine("Cel 01", "127.0.0.1", "S", "AGENT", Kind: DeviceKind.Android);
        using var master = SelfSignedCertificate.Create("master");
        Assert.Throws<ArgumentOutOfRangeException>(() => PhoneNetworkClient.Free(phone, 61, master, 1));
        Assert.Throws<ArgumentException>(() => PhoneNetworkClient.SetAdminPassword(phone, new byte[] { 1, 2, 3 }, master, 1));
        Assert.Throws<InvalidOperationException>(() => PhoneNetworkClient.Free(phone with { AgentThumbprint = null }, 10, master, 1));
    }

    private sealed class ScriptedPhone : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _agentCertificate = SelfSignedCertificate.Create("scripted-phone");
        private volatile Message? _request;

        public ScriptedPhone(Message reply)
        {
            MasterCertificate = SelfSignedCertificate.Create("master-under-test");
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Factory.StartNew(() =>
            {
                try
                {
                    using var client = _listener.AcceptTcpClient();
                    using var channel = SecureChannel.AuthenticateAsServer(
                        client, _agentCertificate, new TrustStore(new[] { MasterCertificate.Thumbprint }));
                    _request = channel.Receive();
                    channel.Send(reply);
                    Thread.Sleep(200);
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public int Port { get; }

        public X509Certificate2 MasterCertificate { get; }

        public KnownMachine Machine => new("Cel 01", "127.0.0.1", "unused-session-thumb", _agentCertificate.Thumbprint, Kind: DeviceKind.Android);

        public Message? Request => _request;

        public void Dispose()
        {
            _listener.Stop();
            _agentCertificate.Dispose();
            MasterCertificate.Dispose();
        }
    }
}
