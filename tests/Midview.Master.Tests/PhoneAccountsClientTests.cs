using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Drives PhoneAccountsClient against a scripted stand-in for a device's
// agent (never a real device): one that knows "Contas do aparelho", "Depuração
// USB" and "Sair das contas do Chrome", and an older one.
public sealed class PhoneAccountsClientTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private static Task<T> Run<T>(Func<T> call) =>
        Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(Limit);

    private static Message Ack(bool succeeded, string detail) => new(MessageType.CommandAck, CommandResult.Encode(succeeded, detail));

    [Fact]
    public async Task Free_sends_109_with_the_minutes()
    {
        using var phone = new ScriptedPhone(Ack(true, "contas do aparelho liberadas por 30 minutos"));

        var answer = await Run(() => PhoneAccountsClient.Free(phone.Machine, 30, phone.MasterCertificate, phone.Port));

        Assert.Equal(MessageType.FreePhoneAccounts, phone.Request!.Type);
        Assert.Equal(new byte[] { 30 }, phone.Request.Payload);
        Assert.Equal((true, "contas do aparelho liberadas por 30 minutos"), answer);
    }

    [Fact]
    public async Task Lock_sends_109_with_zero()
    {
        using var phone = new ScriptedPhone(Ack(true, "contas do aparelho travadas"));

        await Run(() => PhoneAccountsClient.Free(phone.Machine, PhoneNetworkFree.LockNow, phone.MasterCertificate, phone.Port));

        Assert.Equal(new byte[] { 0 }, phone.Request!.Payload);
    }

    [Fact]
    public async Task Free_debugging_sends_111_with_the_minutes_or_zero()
    {
        using var phone = new ScriptedPhone(Ack(true, "depuração USB liberada e ligada por 10 minutos"));

        var answer = await Run(() => PhoneAccountsClient.FreeDebugging(phone.Machine, 10, phone.MasterCertificate, phone.Port));

        Assert.Equal(MessageType.FreePhoneDebugging, phone.Request!.Type);
        Assert.Equal(new byte[] { 10 }, phone.Request.Payload);
        Assert.Equal((true, "depuração USB liberada e ligada por 10 minutos"), answer);

        using var locking = new ScriptedPhone(Ack(true, "depuração USB travada"));
        await Run(() => PhoneAccountsClient.FreeDebugging(locking.Machine, PhoneNetworkFree.LockNow, locking.MasterCertificate, locking.Port));
        Assert.Equal(new byte[] { 0 }, locking.Request!.Payload);
    }

    // An agent from before "Depuração USB": told to update the devices.
    [Fact]
    public async Task An_older_device_without_debugging_is_asked_to_update()
    {
        using var phone = new ScriptedPhone(Ack(false, "Ainda não funciona em celular."));

        var answer = await Run(() => PhoneAccountsClient.FreeDebugging(phone.Machine, 30, phone.MasterCertificate, phone.Port));

        Assert.Null(answer);
        Assert.Equal(PhoneAccountsText.OldAgent, PhoneAccountsText.Line(answer));
    }

    [Fact]
    public async Task Clear_chrome_sends_110_with_nothing()
    {
        using var phone = new ScriptedPhone(Ack(true, "Chrome limpo"));

        var answer = await Run(() => PhoneAccountsClient.ClearChrome(phone.Machine, phone.MasterCertificate, phone.Port));

        Assert.Equal(MessageType.ClearChromeData, phone.Request!.Type);
        Assert.Empty(phone.Request.Payload);
        Assert.Equal("Chrome limpo", PhoneAccountsText.Line(answer));
    }

    // An agent from before these commands: told to update the devices.
    [Theory]
    [InlineData("Ainda não funciona em celular.")]
    [InlineData("")]
    public async Task An_older_device_is_null_and_asked_to_update(string detail)
    {
        using var phone = new ScriptedPhone(Ack(false, detail));

        var answer = await Run(() => PhoneAccountsClient.ClearChrome(phone.Machine, phone.MasterCertificate, phone.Port));

        Assert.Null(answer);
        Assert.Equal(PhoneAccountsText.OldAgent, PhoneAccountsText.Line(answer));
        Assert.Contains("atualize os celulares e tablets", PhoneAccountsText.OldAgent);
    }

    [Fact]
    public async Task A_refusal_is_shown_as_the_agent_said_it()
    {
        using var phone = new ScriptedPhone(Ack(false, "o Chrome não está instalado neste aparelho"));

        var answer = await Run(() => PhoneAccountsClient.ClearChrome(phone.Machine, phone.MasterCertificate, phone.Port));

        Assert.Equal("o Chrome não está instalado neste aparelho", PhoneAccountsText.Line(answer));
    }

    [Fact]
    public void Bad_input_is_refused_before_connecting()
    {
        var phone = new KnownMachine("Cel 01", "127.0.0.1", "S", "AGENT", Kind: DeviceKind.Android);
        using var master = SelfSignedCertificate.Create("master");
        Assert.Throws<ArgumentOutOfRangeException>(() => PhoneAccountsClient.Free(phone, 61, master, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PhoneAccountsClient.FreeDebugging(phone, 61, master, 1));
        Assert.Throws<InvalidOperationException>(() => PhoneAccountsClient.ClearChrome(phone with { AgentThumbprint = null }, master, 1));
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

        public KnownMachine Machine => new("Tab 01", "127.0.0.1", "unused-session-thumb", _agentCertificate.Thumbprint, Kind: DeviceKind.Android);

        public Message? Request => _request;

        public void Dispose()
        {
            _listener.Stop();
            _agentCertificate.Dispose();
            MasterCertificate.Dispose();
        }
    }
}
