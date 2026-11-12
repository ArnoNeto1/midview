using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Drives PhonePinClient against a scripted stand-in for a phone's agent
// (never a real phone): the agent of 0.0.57 and later, and an older one.
public sealed class PhonePinClientTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private static Task<T> Run<T>(Func<T> call) =>
        Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(Limit);

    private static Message Ack(bool succeeded, string detail) => new(MessageType.CommandAck, CommandResult.Encode(succeeded, detail));

    [Fact]
    public async Task Set_sends_100_with_the_four_digits_and_reads_the_answer()
    {
        using var phone = new ScriptedPhone(Ack(true, "PIN posto no celular"));

        var answer = await Run(() => PhonePinClient.Send(phone.Machine, "1234", phone.MasterCertificate, phone.Port));

        Assert.Equal(MessageType.SetPhoneScreenPassword, phone.Request!.Type);
        Assert.Equal(Encoding.ASCII.GetBytes("1234"), phone.Request.Payload);
        Assert.Equal((true, "PIN posto no celular"), answer);
        Assert.Equal("PIN posto no celular", PhonePinText.Line(answer));
    }

    [Fact]
    public async Task Remove_sends_100_with_nothing()
    {
        using var phone = new ScriptedPhone(Ack(true, "PIN tirado do celular"));

        var answer = await Run(() => PhonePinClient.Send(phone.Machine, PhoneScreenPassword.None, phone.MasterCertificate, phone.Port));

        Assert.Equal(MessageType.SetPhoneScreenPassword, phone.Request!.Type);
        Assert.Empty(phone.Request.Payload);
        Assert.Equal((true, "PIN tirado do celular"), answer);
    }

    // A phone that already had a lock: the agent's reset key waits for one unlock.
    [Fact]
    public async Task A_phone_whose_key_is_not_active_yet_says_to_unlock_it_once()
    {
        using var phone = new ScriptedPhone(Ack(false, PhonePinText.AgentTokenPending));

        var answer = await Run(() => PhonePinClient.Send(phone.Machine, "1234", phone.MasterCertificate, phone.Port));

        Assert.Equal((false, PhonePinText.AgentTokenPending), answer);
        Assert.Equal(PhonePinText.TokenPending, PhonePinText.Line(answer));
    }

    [Fact]
    public async Task Any_other_refusal_is_shown_as_the_agent_said_it()
    {
        using var phone = new ScriptedPhone(Ack(false, "o Android recusou o PIN"));

        var answer = await Run(() => PhonePinClient.Send(phone.Machine, "1234", phone.MasterCertificate, phone.Port));

        Assert.Equal("o Android recusou o PIN", PhonePinText.Line(answer));
    }

    // An agent from before the PIN fails every command it doesn't know.
    [Theory]
    [InlineData("Ainda não funciona em celular.")]
    [InlineData("")]
    public async Task An_older_phone_is_null(string detail)
    {
        using var phone = new ScriptedPhone(Ack(false, detail));

        var answer = await Run(() => PhonePinClient.Send(phone.Machine, "1234", phone.MasterCertificate, phone.Port));

        Assert.Null(answer);
        Assert.Equal("o Midview deste celular é de uma versão anterior e ainda não põe o PIN", PhonePinText.Line(answer));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("１２３４")]
    public void A_pin_that_is_not_four_digits_is_refused_before_connecting(string pin) =>
        Assert.Throws<ArgumentException>(() => PhonePinClient.Send(
            new KnownMachine("Cel 01", "127.0.0.1", "S", "AGENT", Kind: DeviceKind.Android), pin,
            SelfSignedCertificate.Create("master"), 1));

    [Fact]
    public void A_phone_without_identity_is_refused_before_connecting() =>
        Assert.Throws<InvalidOperationException>(() => PhonePinClient.Send(
            new KnownMachine("Cel 01", "127.0.0.1", "S", null, Kind: DeviceKind.Android), "1234",
            SelfSignedCertificate.Create("master"), 1));

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
