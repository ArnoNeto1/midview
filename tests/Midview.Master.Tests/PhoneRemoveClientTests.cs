using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Drives PhoneRemoveClient against a scripted stand-in for a phone's agent
// (never a real phone): the undo goes as 106 with the typed word, the
// factory reset as the wipe, and an older agent is told apart.
public sealed class PhoneRemoveClientTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private static Task<T> Run<T>(Func<T> call) =>
        Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(Limit);

    private static Message Ack(bool succeeded, string detail) => new(MessageType.CommandAck, CommandResult.Encode(succeeded, detail));

    [Fact]
    public async Task Undo_sends_106_with_the_word()
    {
        using var phone = new ScriptedPhone(Ack(true, "Midview saindo: travas e ajustes da escola desfeitos."));

        var answer = await Run(() => PhoneRemoveClient.Send(phone.Machine, PhoneRemoveChoice.Undo, phone.MasterCertificate, phone.Port));

        Assert.Equal(MessageType.RemoveMidviewFromPhone, phone.Request!.Type);
        Assert.Equal("REMOVER"u8.ToArray(), phone.Request.Payload);
        Assert.Equal((true, "Midview saindo: travas e ajustes da escola desfeitos."), answer);
    }

    [Fact]
    public async Task Factory_reset_sends_the_wipe()
    {
        using var phone = new ScriptedPhone(Ack(true, "apagando"));

        var answer = await Run(() => PhoneRemoveClient.Send(phone.Machine, PhoneRemoveChoice.FactoryReset, phone.MasterCertificate, phone.Port));

        Assert.Equal(MessageType.WipePhone, phone.Request!.Type);
        Assert.Empty(phone.Request.Payload);
        Assert.Equal((true, "apagando"), answer);
    }

    [Theory]
    [InlineData("Ainda não funciona em celular.")]
    [InlineData("")]
    public async Task An_older_phone_is_null(string detail)
    {
        using var phone = new ScriptedPhone(Ack(false, detail));

        var answer = await Run(() => PhoneRemoveClient.Send(phone.Machine, PhoneRemoveChoice.Undo, phone.MasterCertificate, phone.Port));

        Assert.Null(answer);
        Assert.Equal(PhoneRemoveText.OldAgent, PhoneRemoveText.Line(PhoneRemoveChoice.Undo, answer));
    }

    [Fact]
    public void A_device_without_its_identity_is_refused_before_connecting()
    {
        var phone = new KnownMachine("Cel 01", "127.0.0.1", "s", "", Kind: DeviceKind.Android);
        using var certificate = SelfSignedCertificate.Create("master-under-test");

        Assert.Throws<InvalidOperationException>(() => PhoneRemoveClient.Send(phone, PhoneRemoveChoice.Undo, certificate, 1));
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
