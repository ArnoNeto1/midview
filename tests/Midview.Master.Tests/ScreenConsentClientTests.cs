using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Controls;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Drives ScreenConsentClient against a scripted stand-in for a phone's agent
// (never a real phone), and checks which tiles' menus carry the item.
public sealed class ScreenConsentClientTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private static Task<T> Run<T>(Func<T> call) =>
        Task.Factory.StartNew(call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(Limit);

    [Fact]
    public async Task Ask_sends_request_and_reads_ack()
    {
        using var phone = new ScriptedPhone(channel =>
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, "pedido mostrado"))));

        var (succeeded, detail) = await Run(() => ScreenConsentClient.Ask(phone.Machine, phone.MasterCertificate, phone.Port));

        Assert.True(succeeded);
        Assert.Equal("pedido mostrado", detail);
        Assert.Equal(MessageType.RequestScreenConsent, phone.Request!.Type);
        Assert.Empty(phone.Request.Payload);
    }

    [Fact]
    public async Task Ask_reads_a_failure_from_the_phone()
    {
        using var phone = new ScriptedPhone(channel =>
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(false, "a tela está bloqueada"))));

        var (succeeded, detail) = await Run(() => ScreenConsentClient.Ask(phone.Machine, phone.MasterCertificate, phone.Port));

        Assert.False(succeeded);
        Assert.Equal("a tela está bloqueada", detail);
    }

    [Fact]
    public void Status_line_names_the_phone_and_the_failure()
    {
        Assert.Equal("Pedido mostrado no celular Cel 01.", ScreenConsentClient.Summary("Cel 01", true, "pedido mostrado"));
        Assert.Equal("Cel 01: a tela já está liberada", ScreenConsentClient.Summary("Cel 01", true, "a tela já está liberada"));
        Assert.Equal("Cel 01: a tela está bloqueada", ScreenConsentClient.Summary("Cel 01", false, "a tela está bloqueada"));
    }

    [Fact]
    public void Menu_item_only_for_phones()
    {
        var pc = new KnownMachine("PC-1", "10.0.0.1", "s", "a");
        var phone = new KnownMachine("Cel 01", "10.0.0.2", "s", "a", Kind: DeviceKind.Android);
        Assert.True(phone.IsPhone);

        InSta(() =>
        {
            var menu = new ContextMenu();
            var item = new MenuItem { Tag = MainWindow.PhoneOnlyTag, Header = ScreenConsentClient.MenuText, Visibility = System.Windows.Visibility.Collapsed };
            var other = new MenuItem { Header = "Ligar" };
            menu.Items.Add(item);
            menu.Items.Add(other);

            MainWindow.ShowPhoneItems(menu, new[] { pc });
            Assert.Equal(System.Windows.Visibility.Collapsed, item.Visibility);
            Assert.Equal(System.Windows.Visibility.Visible, other.Visibility);

            MainWindow.ShowPhoneItems(menu, new[] { pc, phone });
            Assert.Equal(System.Windows.Visibility.Visible, item.Visibility);

            MainWindow.ShowPhoneItems(menu, new[] { phone });
            Assert.Equal(System.Windows.Visibility.Visible, item.Visibility);
            Assert.Equal("Pedir de novo para ver a tela", item.Header);
        });
    }

    private static void InSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); } catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    // Answers the first message with whatever the test scripts, then hangs up.
    private sealed class ScriptedPhone : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _agentCertificate = SelfSignedCertificate.Create("scripted-phone");
        private volatile Message? _request;

        public ScriptedPhone(Action<SecureChannel> script)
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
                    script(channel);
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
