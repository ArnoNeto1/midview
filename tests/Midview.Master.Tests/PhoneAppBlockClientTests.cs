using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Drives PhoneAppBlockClient against a scripted stand-in for a phone's agent
// (never a real phone).
public sealed class PhoneAppBlockClientTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private static Task<T> Run<T>(Func<T> call) =>
        Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(Limit);

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47 };

    [Fact]
    public async Task GetStatus_reads_the_policy_the_phone_follows()
    {
        var followed = new PhoneAppPolicy(1000, AppBlockMode.BlockList, new[] { "com.google.android.youtube" });
        using var phone = new ScriptedPhone(new Message(MessageType.PhoneAppBlockStatus, followed.Encode()));

        var status = await Run(() => PhoneAppBlockClient.GetStatus(phone.Machine, phone.MasterCertificate, phone.Port));

        Assert.False(status.OldAgent);
        Assert.True(followed.SameRulesAs(status.Policy!));
        Assert.Equal(1000, status.Policy!.SavedAt);
        Assert.Equal(MessageType.GetPhoneAppBlock, phone.Request!.Type);
        Assert.Empty(phone.Request.Payload);
    }

    // A phone agent from before app blocking answers every unknown command
    // with a CommandAck ("Ainda não funciona em celular.").
    [Fact]
    public async Task GetStatus_an_older_phone_is_reported_as_old()
    {
        using var phone = new ScriptedPhone(new Message(MessageType.CommandAck, CommandResult.Encode(false, "Ainda não funciona em celular.")));

        var status = await Run(() => PhoneAppBlockClient.GetStatus(phone.Machine, phone.MasterCertificate, phone.Port));

        Assert.True(status.OldAgent);
        Assert.Null(status.Policy);
    }

    [Fact]
    public async Task GetStatus_a_policy_that_does_not_decode_is_refused()
    {
        using var phone = new ScriptedPhone(new Message(MessageType.PhoneAppBlockStatus, new byte[] { 9, 9 }));

        await Assert.ThrowsAsync<InvalidDataException>(() => Run(() => PhoneAppBlockClient.GetStatus(phone.Machine, phone.MasterCertificate, phone.Port)));
    }

    [Fact]
    public async Task Apply_sends_the_policy_and_reads_what_the_phone_did()
    {
        var policy = new PhoneAppPolicy(2000, AppBlockMode.AllowOnly, new[] { "com.android.chrome" });
        using var phone = new ScriptedPhone(new Message(MessageType.CommandAck, CommandResult.Encode(true, "permitindo só o essencial e 1 app(s)")));

        var (succeeded, detail) = await Run(() => PhoneAppBlockClient.Apply(phone.Machine, phone.MasterCertificate, policy, phone.Port));

        Assert.True(succeeded);
        Assert.Equal("permitindo só o essencial e 1 app(s)", detail);
        Assert.Equal(MessageType.SetPhoneAppBlock, phone.Request!.Type);
        Assert.Equal(policy.Encode(), phone.Request.Payload);
    }

    [Fact]
    public async Task ListApps_reads_the_apps_with_their_icons()
    {
        var apps = new[]
        {
            new PhoneApp("com.android.chrome", "Chrome", Png),
            new PhoneApp("com.google.android.youtube", "YouTube", Png),
        };
        using var phone = new ScriptedPhone(new Message(MessageType.PhoneApps, PhoneAppList.Encode(apps)));

        var listed = await Run(() => PhoneAppBlockClient.ListApps(phone.Machine, phone.MasterCertificate, phone.Port));

        Assert.NotNull(listed);
        Assert.Equal(new[] { "com.android.chrome", "com.google.android.youtube" }, listed!.Select(app => app.Package));
        Assert.Equal("YouTube", listed[1].Label);
        Assert.Equal(Png, listed[1].IconPng);
        Assert.Equal(MessageType.GetPhoneApps, phone.Request!.Type);
    }

    [Fact]
    public async Task ListApps_an_older_phone_has_no_list()
    {
        using var phone = new ScriptedPhone(new Message(MessageType.CommandAck, CommandResult.Encode(false, "Ainda não funciona em celular.")));

        Assert.Null(await Run(() => PhoneAppBlockClient.ListApps(phone.Machine, phone.MasterCertificate, phone.Port)));
    }

    [Fact]
    public async Task ListApps_a_list_that_does_not_decode_is_refused()
    {
        using var phone = new ScriptedPhone(new Message(MessageType.PhoneApps, new byte[] { 1, 0, 5 }));

        await Assert.ThrowsAsync<InvalidDataException>(() => Run(() => PhoneAppBlockClient.ListApps(phone.Machine, phone.MasterCertificate, phone.Port)));
    }

    [Fact]
    public void A_phone_without_its_agents_identity_is_not_asked()
    {
        using var certificate = SelfSignedCertificate.Create("master-under-test");
        var phone = new KnownMachine("Cel 01", "127.0.0.1", "s", null, Kind: DeviceKind.Android);

        Assert.Throws<InvalidOperationException>(() => PhoneAppBlockClient.GetStatus(phone, certificate, 1));
        Assert.Throws<InvalidOperationException>(() => PhoneAppBlockClient.ListApps(phone, certificate, 1));
        Assert.Throws<InvalidOperationException>(() => PhoneAppBlockClient.Apply(phone, certificate, PhoneAppPolicy.Lifted(1), 1));
    }

    // Answers the first message with the scripted reply, then hangs up.
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
