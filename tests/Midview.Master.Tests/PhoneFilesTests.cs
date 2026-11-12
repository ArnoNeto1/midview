using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// "Enviar arquivo" and "Recolher arquivos" with phones: a phone's agent (port
// 28900, its own thumbprint) takes the PC's exchanges for its
// Download/Midview, an older agent is named as such, and the windows say
// where the files are.
public class PhoneFilesTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), $"midview-phone-files-{Guid.NewGuid():N}");

    public PhoneFilesTests() => Directory.CreateDirectory(_work);

    public void Dispose() => Directory.Delete(_work, recursive: true);

    private static KnownMachine Phone(string name = "Celular 01", string? agent = "AB12") =>
        new(name, "127.0.0.1", "SESSION-NOT-USED", agent, Kind: DeviceKind.Android);

    private static KnownMachine Pc(string name = "ALUNO-01") => new(name, "127.0.0.1", "ANY", "AGENT");

    [Fact]
    public void The_windows_say_where_the_files_go_on_a_phone()
    {
        var pcs = new[] { Pc() };
        var phones = new[] { Phone() };
        var both = new[] { Pc(), Phone() };

        Assert.Equal(FileTargets.SendPcHint, FileTargets.SendHint(pcs));
        Assert.Equal(FileTargets.SendPhoneHint, FileTargets.SendHint(phones));
        Assert.Equal(FileTargets.SendPcHint + FileTargets.SendPhoneNote, FileTargets.SendHint(both));
        Assert.Contains("Download/Midview", FileTargets.SendPhoneHint);

        Assert.Equal(FileTargets.CollectPcHint, FileTargets.CollectHint(pcs));
        Assert.Equal(FileTargets.CollectPhoneHint, FileTargets.CollectHint(phones));
        Assert.Equal(FileTargets.CollectPcHint + FileTargets.CollectPhoneNote, FileTargets.CollectHint(both));

        Assert.Equal(FileTargets.ClearPcHint, FileTargets.ClearHint(pcs));
        Assert.Contains("apagado de vez", FileTargets.ClearHint(phones));
        Assert.StartsWith(FileTargets.ClearPcHint, FileTargets.ClearHint(both));

        Assert.Equal("Abrir a pasta \"Arquivos do professor\" no aluno", FileTargets.OpenFolderChoice(pcs));
        Assert.Equal("Abrir a pasta Download/Midview no celular", FileTargets.OpenFolderChoice(phones));
        Assert.Equal("Abrir o arquivo no celular", FileTargets.OpenFileChoice(phones));
        Assert.Equal("Abrir ou executar o arquivo no aluno", FileTargets.OpenFileChoice(both));

        Assert.Equal("Para 2 celulares: Celular 01, Celular 02",
            FileTargets.Describe(TargetsPreposition.To, new[] { Phone(), Phone("Celular 02") }, wholeClass: false));
        Assert.Equal("Da turma toda (2 máquinas)", FileTargets.Describe(TargetsPreposition.From, both, wholeClass: true));
    }

    // A phone has no Recycle Bin: emptying it is asked first; PCs alone are not.
    [Fact]
    public void Emptying_the_phones_folder_asks_first()
    {
        Assert.Null(FileTargets.ClearQuestion(new[] { Pc() }));
        Assert.Equal(
            "O que está na pasta Download/Midview do celular vai ser apagado de vez (no celular não há Lixeira), inclusive os arquivos que o professor enviou.",
            FileTargets.ClearQuestion(new[] { Phone() }));
        Assert.Equal(
            "O que está na pasta Download/Midview dos 2 celulares vai ser apagado de vez (no celular não há Lixeira), inclusive os arquivos que o professor enviou. Nos computadores, vai para a Lixeira, como sempre.",
            FileTargets.ClearQuestion(new[] { Pc(), Phone(), Phone("Celular 02") }));
    }

    [Fact]
    public void A_phone_without_its_identity_is_not_asked()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        var file = WriteSource("a.txt", new byte[] { 1 });
        Assert.Equal((false, PhoneAgentCommand.NoIdentity), FileTargets.Send(Phone(agent: null), master, file, SendFileOptions.Default));
        Assert.Equal(PhoneAgentCommand.NoIdentity, FileTargets.Collect(Phone(agent: null), master, Destination).Detail);
        Assert.Equal((false, PhoneAgentCommand.NoIdentity), FileTargets.Clear(Phone(agent: null), master));
    }

    [Fact]
    public async Task A_file_goes_to_the_phones_agent_with_its_options()
    {
        var content = Enumerable.Range(0, FileTransfer.ChunkSize + 77).Select(i => (byte)(i * 3)).ToArray();
        var source = WriteSource("Lista 1.pdf", content);
        byte[]? received = null;
        using var agent = new FakeAgent(channel =>
        {
            var header = channel.Receive();
            Assert.Equal(MessageType.SendFileWithOptions, header.Type);
            Assert.True(FileTransfer.TryDecodeOptionsHeader(header.Payload, out var options, out var name, out var size));
            Assert.Equal(new SendFileOptions(true, AfterSend.OpenFile), options);
            Assert.Equal("Lista 1.pdf", name);
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, "")));
            using var data = new MemoryStream();
            while (data.Length < size)
            {
                data.Write(channel.Receive().Payload);
            }
            received = data.ToArray();
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, "Lista 1.pdf; aberto no aluno")));
        });

        var result = await Task.Run(() => FileTargets.Send(
            agent.Phone, agent.MasterCertificate, source, new SendFileOptions(true, AfterSend.OpenFile), agent.Port))
            .WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal((true, "Lista 1.pdf; aberto no aluno"), result);
        Assert.Equal(content, received);
    }

    [Fact]
    public async Task An_older_phone_agent_is_named_as_such()
    {
        var source = WriteSource("a.txt", new byte[] { 1 });
        foreach (var answer in new[] { "Ainda não funciona em celular.", "" })
        {
            using var sending = new FakeAgent(OldAgent(answer));
            Assert.Equal((false, FileTargets.OldAgentSend), await Task.Run(() =>
                FileTargets.Send(sending.Phone, sending.MasterCertificate, source, SendFileOptions.Default, sending.Port)).WaitAsync(TimeSpan.FromSeconds(20)));

            using var collecting = new FakeAgent(OldAgent(answer));
            var collected = await Task.Run(() =>
                FileTargets.Collect(collecting.Phone, collecting.MasterCertificate, Destination, collecting.Port)).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(new CollectResult(false, 0, 0, FileTargets.OldAgentCollect), collected);

            using var clearing = new FakeAgent(OldAgent(answer));
            Assert.Equal((false, FileTargets.OldAgentClear), await Task.Run(() =>
                FileTargets.Clear(clearing.Phone, clearing.MasterCertificate, clearing.Port)).WaitAsync(TimeSpan.FromSeconds(20)));
        }
        Assert.Equal("o Midview deste celular é de uma versão anterior e ainda não recebe arquivos: atualize-o", FileTargets.OldAgentSend);
    }

    // Whatever the phone sends is checked as a PC's is (FileCollector).
    [Fact]
    public async Task A_phones_files_are_collected_like_a_pcs()
    {
        using var agent = new FakeAgent(channel =>
        {
            Assert.Equal(MessageType.CollectFiles, channel.Receive().Type);
            SendFile(channel, "foto.jpg", new byte[] { 1, 2, 3 });
            SendFile(channel, "../../fora.txt", new byte[] { 4 });
            SendFile(channel, "sub/trabalho.docx", new byte[] { 5, 6 });
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, "")));
        });

        var result = await Task.Run(() => FileTargets.Collect(agent.Phone, agent.MasterCertificate, Destination, agent.Port))
            .WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(new CollectResult(true, 3, 6, ""), result);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Destination, "foto.jpg")));
        Assert.Equal(new byte[] { 4 }, File.ReadAllBytes(Path.Combine(Destination, "fora.txt")));
        Assert.Equal(new byte[] { 5, 6 }, File.ReadAllBytes(Path.Combine(Destination, "sub", "trabalho.docx")));
    }

    // What the phone said about its collection reaches the teacher as it came.
    [Fact]
    public async Task What_the_phone_says_about_its_folder_is_kept()
    {
        const string Said = "sem acesso a todos os arquivos neste celular: só os que o próprio Midview salvou";
        using var agent = new FakeAgent(channel =>
        {
            Assert.Equal(MessageType.CollectFiles, channel.Receive().Type);
            SendFile(channel, "aula.pdf", new byte[] { 1 });
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, Said)));
        });
        var result = await Task.Run(() => FileTargets.Collect(agent.Phone, agent.MasterCertificate, Destination, agent.Port))
            .WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(new CollectResult(true, 1, 1, Said), result);
    }

    [Fact]
    public async Task Emptying_a_phones_folder_brings_back_its_words()
    {
        using var agent = new FakeAgent(channel =>
        {
            Assert.Equal(MessageType.ClearDeliveryFolder, channel.Receive().Type);
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, "2 item(ns) foram apagados")));
        });
        Assert.Equal((true, "2 item(ns) foram apagados"), await Task.Run(() =>
            FileTargets.Clear(agent.Phone, agent.MasterCertificate, agent.Port)).WaitAsync(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void A_phone_that_is_not_running_says_so()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        var free = new TcpListener(IPAddress.Loopback, 0);
        free.Start();
        var port = ((IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        Assert.Equal("O Midview não está rodando neste celular", FileTargets.Clear(Phone(), master, port).Detail);
    }

    // The collected files a phone's old agent left counted before it failed stay counted.
    [Fact]
    public void A_phones_failure_keeps_what_was_saved() =>
        Assert.Equal(new CollectResult(false, 2, 10, "a conexão caiu"),
            FileTargets.FromPhone(new CollectResult(false, 2, 10, "a conexão caiu"), false, "a conexão caiu"));

    private string Destination => Path.Combine(_work, "recolhidos", "Celular 01");

    private string WriteSource(string name, byte[] content)
    {
        var path = Path.Combine(_work, "fonte", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static Action<SecureChannel> OldAgent(string answer) => channel =>
    {
        channel.Receive();
        channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(false, answer)));
    };

    private static void SendFile(SecureChannel channel, string name, byte[] content)
    {
        channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader(name, content.Length)));
        channel.Send(new Message(MessageType.FileChunk, content));
    }

    // A phone's agent: its own certificate (the phone's AgentThumbprint, not
    // its SessionThumbprint), the teacher's checked, then the test's script.
    private sealed class FakeAgent : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _agentCertificate = SelfSignedCertificate.Create("phone-agent");

        public FakeAgent(Action<SecureChannel> script)
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
                        client, _agentCertificate, new TrustStore(new[] { MasterCertificate.Thumbprint }));
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

        public KnownMachine Phone => PhoneFilesTests.Phone(agent: _agentCertificate.Thumbprint);

        public void Dispose()
        {
            _listener.Stop();
            _agentCertificate.Dispose();
            MasterCertificate.Dispose();
        }
    }
}
