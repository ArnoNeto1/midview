using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// "Pasta da turma" with phones: a phone's agent takes the PC Agent's lesson
// messages on 28900 and the PC Session's folder messages on its screen port,
// and an agent from before it reads "Midview antigo", as an old PC does.
public class PhoneClassFolderTests
{
    private static readonly ClassLesson Lesson = new(1000, Guid.NewGuid(), "3º A", 1, Ended: false);

    [Fact]
    public void An_older_phone_agent_is_an_older_midview_for_the_lesson()
    {
        using var agent = new FakePhone(channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(false, PhoneAgentCommand.NotOnPhone)));
        });
        Assert.Equal((false, CommandClient.OlderSession), ClassFolderClient.SetLesson(agent.Phone, agent.MasterCertificate, Lesson, agent.Port));
    }

    [Fact]
    public void An_older_phone_agent_keeps_no_home()
    {
        using var agent = new FakePhone(channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(false, PhoneAgentCommand.NotOnPhone)));
        });
        var (ok, _, oldAgent) = ClassFolderClient.ClaimHome(
            agent.Phone, agent.MasterCertificate, new ClassHomeClaim("PROF-01", ClassFolderRemote.Port, false, 5), agent.Port);
        Assert.False(ok);
        Assert.True(oldAgent);
    }

    [Fact]
    public void An_older_phone_hangs_up_on_the_listing_and_reads_as_an_older_midview()
    {
        using var phone = new FakePhone(channel => channel.Receive());
        var listing = ClassFolderClient.List(phone.Phone, phone.MasterCertificate, phone.Port);
        Assert.True(listing.OldSession);
    }

    [Fact]
    public void A_phone_lists_its_class_folder_as_a_session_does()
    {
        var entries = new[] { new ClassFolderEntry(Lesson.LessonId, "trabalho.docx", 12, 638_600_000_000_001_234) };
        using var phone = new FakePhone(channel =>
        {
            Assert.Equal(MessageType.ListClassFolder, channel.Receive().Type);
            foreach (var part in ClassFolderWire.EncodeListingParts(Lesson.LessonId, null, entries))
            {
                channel.Send(new Message(MessageType.ClassFolderListing, part));
            }
        });
        var listing = ClassFolderClient.List(phone.Phone, phone.MasterCertificate, phone.Port);
        Assert.False(listing.OldSession);
        Assert.Equal(Lesson.LessonId, listing.CurrentLessonId);
        Assert.Equal(entries, listing.Entries);
    }

    // A PC that hangs up is not taken for an older one: that is a failure, said as one.
    [Fact]
    public void A_pc_that_hangs_up_is_a_failure_not_an_older_midview()
    {
        using var pc = new FakePhone(channel => channel.Receive());
        Assert.ThrowsAny<IOException>(() => ClassFolderClient.List(pc.Phone with { Kind = DeviceKind.Windows }, pc.MasterCertificate, pc.Port));
    }

    [Fact]
    public void Texts_say_phones_when_the_room_has_them()
    {
        Assert.Equal("A pasta da turma vale para todos os computadores da sala.", ClassFolderText.WholeRoom(phones: false));
        Assert.Equal("A pasta da turma vale para todos os computadores e celulares da sala.", ClassFolderText.WholeRoom(phones: true));
        Assert.Equal("A aula do 3º A começou em todos os 3 PCs.", ClassFolderText.Outcome(true, "3º A", 3, 3, 0));
        Assert.Equal("A aula do 3º A começou em todas as 3 máquinas.", ClassFolderText.Outcome(true, "3º A", 3, 3, 0, phones: true));
        Assert.Equal("A aula do 3º A começou em 2 de 3 máquinas; os desligados recebem a pasta ao ligar.",
            ClassFolderText.Outcome(true, "3º A", 2, 3, 1, phones: true));
        Assert.Equal("Voltam para todos os computadores e celulares na próxima aula do 3º A.",
            ClassFolderText.EndChoiceHint(EndChoice.Keep, "3º A", 0, 0, false, phones: true));
        Assert.Equal("O arquivo sai do seu PC e de todos os computadores e celulares. Vai para a Lixeira deste PC.",
            ClassFolderText.EndChoiceHint(EndChoice.DeleteAll, "3º A", 0, 1, false, phones: true));
    }

    // A phone's agent: one certificate for both its ports (the test's script
    // on one of them), the teacher's checked first.
    private sealed class FakePhone : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _certificate = SelfSignedCertificate.Create("phone-agent");

        public FakePhone(Action<SecureChannel> script)
        {
            MasterCertificate = SelfSignedCertificate.Create("master-under-test");
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(() =>
            {
                try
                {
                    using var client = _listener.AcceptTcpClient();
                    using var channel = SecureChannel.AuthenticateAsServer(
                        client, _certificate, new TrustStore(new[] { MasterCertificate.Thumbprint }));
                    script(channel);
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    // The test is over, or the teacher's side hung up.
                }
            });
        }

        public int Port { get; }

        public X509Certificate2 MasterCertificate { get; }

        public KnownMachine Phone =>
            new("Celular 01", "127.0.0.1", _certificate.Thumbprint, _certificate.Thumbprint, Kind: DeviceKind.Android);

        public void Dispose()
        {
            _listener.Stop();
            _certificate.Dispose();
            MasterCertificate.Dispose();
        }
    }
}
