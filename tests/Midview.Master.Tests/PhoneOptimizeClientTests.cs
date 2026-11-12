using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Drives PhoneOptimizeClient against a scripted stand-in for a phone's agent
// (never a real phone).
public sealed class PhoneOptimizeClientTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private static Task<T> Run<T>(Func<T> call) =>
        Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(Limit);

    [Fact]
    public async Task Optimize_sends_98_with_nothing_and_reads_the_report()
    {
        var report = new PhoneOptimizeReport(
            DateTimeOffset.UnixEpoch, new[] { "com.miui.msa.global", "com.miui.analytics" }, Array.Empty<string>(),
            PhoneDnsStates.On, "dns.adguard-dns.com");
        using var phone = new ScriptedPhone(new Message(MessageType.PhoneOptimizeReport, report.Encode()));

        var answer = await Run(() => PhoneOptimizeClient.Optimize(phone.Machine, phone.MasterCertificate, phone.Port));

        Assert.Equal(MessageType.OptimizePhone, phone.Request!.Type);
        Assert.Empty(phone.Request.Payload);
        Assert.NotNull(answer);
        Assert.Equal(report.Hidden, answer!.Hidden);
        Assert.Equal(PhoneDnsStates.On, answer.Dns);
    }

    // An agent from before "Otimizar" answers every unknown command with a CommandAck.
    [Fact]
    public async Task Optimize_an_older_phone_is_null()
    {
        using var phone = new ScriptedPhone(new Message(MessageType.CommandAck, CommandResult.Encode(false, "Ainda não funciona em celular.")));

        var answer = await Run(() => PhoneOptimizeClient.Optimize(phone.Machine, phone.MasterCertificate, phone.Port));

        Assert.Null(answer);
    }

    [Fact]
    public async Task Optimize_a_report_that_does_not_decode_is_refused()
    {
        using var phone = new ScriptedPhone(new Message(MessageType.PhoneOptimizeReport, new byte[] { 1, 2, 3 }));

        await Assert.ThrowsAsync<InvalidDataException>(() => Run(() => PhoneOptimizeClient.Optimize(phone.Machine, phone.MasterCertificate, phone.Port)));
    }

    // The phone may take ~40 s on a slow network: longer than a usual command's 20 s.
    [Fact]
    public async Task Optimize_waits_past_a_usual_command_timeout()
    {
        var report = new PhoneOptimizeReport(null, Array.Empty<string>(), Array.Empty<string>(), PhoneDnsStates.OffNetworkBlocks, "");
        using var phone = new ScriptedPhone(new Message(MessageType.PhoneOptimizeReport, report.Encode()), delay: TimeSpan.FromSeconds(22));

        var answer = await Task.Factory.StartNew(
                () => PhoneOptimizeClient.Optimize(phone.Machine, phone.MasterCertificate, phone.Port),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(PhoneDnsStates.OffNetworkBlocks, answer!.Dns);
    }

    [Fact]
    public void Optimize_a_phone_without_identity_is_refused_before_connecting() =>
        Assert.Throws<InvalidOperationException>(() => PhoneOptimizeClient.Optimize(
            new KnownMachine("Cel 01", "127.0.0.1", "S", null, Kind: DeviceKind.Android),
            SelfSignedCertificate.Create("master"), 1));

    [Fact]
    public void Read_timeout_is_90_seconds() =>
        Assert.Equal(TimeSpan.FromSeconds(90), PhoneOptimizeClient.ReadTimeout);

    private sealed class ScriptedPhone : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _agentCertificate = SelfSignedCertificate.Create("scripted-phone");
        private volatile Message? _request;

        public ScriptedPhone(Message reply, TimeSpan? delay = null)
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
                    if (delay is { } wait)
                    {
                        Thread.Sleep(wait);
                    }
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
