using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class FileCollectorTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), $"midview-collect-{Guid.NewGuid():N}");

    public FileCollectorTests()
    {
        Directory.CreateDirectory(_work);
    }

    private string StudentFolder => Path.Combine(_work, "aluno", "Entregar ao professor");

    // Inside a "recolhidos" folder of its own, so "nothing outside" can be checked.
    private string Destination => Path.Combine(_work, "recolhidos", "Aluno-1");

    public void Dispose() => Directory.Delete(_work, recursive: true);

    [Fact]
    public async Task CollectsTheWholeFolder_SubfoldersIncluded()
    {
        WriteStudentFile("trabalho.docx", new byte[] { 1, 2, 3 });
        var big = Enumerable.Range(0, FileTransfer.ChunkSize * 2 + 5).Select(i => (byte)(i * 7)).ToArray();
        WriteStudentFile("site/imagens/foto.jpg", big);
        using var session = StartSession();

        var result = await Collect(session);

        Assert.Equal(new CollectResult(true, 2, 3 + big.Length, string.Empty), result);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(Destination, "trabalho.docx")));
        Assert.Equal(big, File.ReadAllBytes(Path.Combine(Destination, "site", "imagens", "foto.jpg")));
        Assert.Empty(Directory.GetFiles(Destination, "*.parcial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CollectingAgain_NeverOverwrites()
    {
        WriteStudentFile("trabalho.docx", new byte[] { 1 });
        using var session = StartSession();
        await Collect(session);
        WriteStudentFile("trabalho.docx", new byte[] { 2 });

        await Collect(session);

        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(Destination, "trabalho.docx")));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(Destination, "trabalho (2).docx")));
    }

    [Fact]
    public async Task EmptyFolder_Succeeds_WithoutCreatingAnything()
    {
        Directory.CreateDirectory(StudentFolder);
        using var session = StartSession();

        var result = await Collect(session);

        Assert.Equal(new CollectResult(true, 0, 0, string.Empty), result);
        Assert.False(Directory.Exists(Destination));
    }

    [Fact]
    public async Task HostileSession_PathsThatClimbOut_AllLandInsideTheDestination()
    {
        using var session = new HostileSession(channel =>
        {
            foreach (var name in new[] { "../../fora.txt", @"..\..\..\fora2.txt", "C:/Windows/System32/evil.exe", "pasta/../../../fora3.txt", "//servidor/x.txt" })
            {
                SendFile(channel, name, new byte[] { 9 });
            }
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, string.Empty)));
        });

        var result = await Collect(session);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Files);
        Assert.Equal(new[] { "Aluno-1" }, Directory.GetFileSystemEntries(Path.GetDirectoryName(Destination)!).Select(Path.GetFileName));
        Assert.Equal(new[] { "fora.txt", "fora2.txt" }, Directory.GetFiles(Destination).Select(Path.GetFileName).Order());
        Assert.True(File.Exists(Path.Combine(Destination, "C_", "Windows", "System32", "evil.exe")));
        Assert.True(File.Exists(Path.Combine(Destination, "pasta", "fora3.txt")));
        Assert.True(File.Exists(Path.Combine(Destination, "servidor", "x.txt")));
    }

    [Fact]
    public async Task AFileThatCantBeSavedHere_IsSkipped_AndTheRestStillArrives()
    {
        using var session = new HostileSession(channel =>
        {
            SendFile(channel, "a", new byte[] { 1 });
            SendFile(channel, "a/b.txt", new byte[] { 2, 2 }); // "a" is a file here, so no folder "a"
            SendFile(channel, "c.txt", new byte[] { 3 });
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, string.Empty)));
        });

        var result = await Collect(session);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Files);
        Assert.Equal(2, result.Bytes);
        Assert.StartsWith("1 não salvo(s) aqui: a/b.txt não pôde ser salvo", result.Detail);
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(Path.Combine(Destination, "c.txt")));
        Assert.Empty(Directory.GetFiles(Destination, "*.parcial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task HostileSession_MoreBytesThanDeclared_IsDiscarded()
    {
        using var session = new HostileSession(channel =>
        {
            SendFile(channel, "ok.txt", new byte[] { 1 });
            channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader("grande.txt", 3)));
            channel.Send(new Message(MessageType.FileChunk, new byte[] { 1, 2, 3, 4 }));
        });

        var result = await Collect(session);

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.Files);
        Assert.Equal(new[] { "ok.txt" }, Directory.GetFileSystemEntries(Destination).Select(Path.GetFileName));
    }

    [Fact]
    public async Task HostileSession_MoreFilesThanTheLimit_Stops()
    {
        using var session = new HostileSession(channel =>
        {
            for (var i = 0; i < 5; i++)
            {
                SendFile(channel, $"f{i}.txt", new byte[] { 1 });
            }
        });

        var result = await Collect(session, maxFiles: 2);

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Files);
        Assert.Contains("mais de 2 arquivos", result.Detail);
        Assert.Equal(2, Directory.GetFiles(Destination).Length);
    }

    [Fact]
    public async Task HostileSession_MoreBytesThanTheLimit_StopsBeforeWritingThem()
    {
        using var session = new HostileSession(channel =>
        {
            SendFile(channel, "a.txt", new byte[6]);
            channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader("b.txt", 6)));
        });

        var result = await Collect(session, maxTotalBytes: 10);

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.Files);
        Assert.Equal(new[] { "a.txt" }, Directory.GetFileSystemEntries(Destination).Select(Path.GetFileName));
    }

    [Fact]
    public async Task HostileSession_DropsMidFile_ThePartIsDiscarded()
    {
        using var session = new HostileSession(channel =>
        {
            channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader("metade.zip", 100)));
            channel.Send(new Message(MessageType.FileChunk, new byte[50]));
        });

        var result = await Collect(session);

        Assert.False(result.Succeeded);
        Assert.StartsWith("a conexão caiu", result.Detail);
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact]
    public async Task HostileSession_AnythingButFiles_Stops()
    {
        using var session = new HostileSession(channel =>
            channel.Send(new Message(MessageType.ShowMessage, TextMessage.Encode("oi"))));

        var result = await Collect(session);

        Assert.False(result.Succeeded);
        Assert.Contains("resposta inesperada", result.Detail);
    }

    private Task<CollectResult> Collect(
        ISessionUnderTest session, int maxFiles = FileCollection.MaxFiles, long maxTotalBytes = FileCollection.MaxTotalBytes) =>
        Task.Run(() => FileCollector.Collect(
            new KnownMachine("Aluno-1", "127.0.0.1", session.Thumbprint, null), session.MasterCertificate,
            Destination, session.Port, maxFiles, maxTotalBytes)).WaitAsync(TimeSpan.FromSeconds(20));

    private void WriteStudentFile(string relativePath, byte[] content)
    {
        var path = Path.Combine(StudentFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private static void SendFile(SecureChannel channel, string name, byte[] content)
    {
        channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader(name, content.Length)));
        if (content.Length > 0)
        {
            channel.Send(new Message(MessageType.FileChunk, content));
        }
    }

    private RealSession StartSession() => new(new DeliveryFolder(StudentFolder));

    private interface ISessionUnderTest
    {
        int Port { get; }

        string Thumbprint { get; }

        X509Certificate2 MasterCertificate { get; }
    }

    private sealed class RealSession : ISessionUnderTest, IDisposable
    {
        private readonly X509Certificate2 _sessionCertificate = SelfSignedCertificate.Create("session-under-test");
        private readonly ThumbnailPollerTests.LoopbackSession _session;

        public RealSession(DeliveryFolder folder)
        {
            MasterCertificate = SelfSignedCertificate.Create("master-under-test");
            _session = new ThumbnailPollerTests.LoopbackSession(
                0, _sessionCertificate, MasterCertificate, deliveryFolder: folder);
        }

        public int Port => _session.Port;

        public string Thumbprint => _sessionCertificate.Thumbprint;

        public X509Certificate2 MasterCertificate { get; }

        public void Dispose()
        {
            _session.Dispose();
            _sessionCertificate.Dispose();
            MasterCertificate.Dispose();
        }
    }

    // Stands in for a Session a student rewrote: accepts the teacher's
    // CollectFiles, then sends whatever the test scripts, then hangs up.
    private sealed class HostileSession : ISessionUnderTest, IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _sessionCertificate = SelfSignedCertificate.Create("hostile-session");

        public HostileSession(Action<SecureChannel> script)
        {
            MasterCertificate = SelfSignedCertificate.Create("master-under-test");
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(() =>
            {
                using var client = _listener.AcceptTcpClient();
                using var channel = SecureChannel.AuthenticateAsServer(
                    client, _sessionCertificate, new TrustStore(new[] { MasterCertificate.Thumbprint }));
                Assert.Equal(MessageType.CollectFiles, channel.Receive().Type);
                script(channel);
            });
        }

        public int Port { get; }

        public string Thumbprint => _sessionCertificate.Thumbprint;

        public X509Certificate2 MasterCertificate { get; }

        public void Dispose()
        {
            _listener.Stop();
            _sessionCertificate.Dispose();
            MasterCertificate.Dispose();
        }
    }
}
