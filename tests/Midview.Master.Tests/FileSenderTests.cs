using Midview.Master;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class FileSenderTests
{
    [Fact]
    public async Task Send_ArrivesIdentical_AndASecondCopyGetsItsOwnName()
    {
        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");
        using var sessionCertificate = SelfSignedCertificate.Create("session-under-test");
        var inboxDirectory = Path.Combine(Path.GetTempPath(), $"midview-inbox-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(Path.GetTempPath(), $"midview-source-{Guid.NewGuid():N}", "Lista de exercícios.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        // A few chunks' worth, so it takes more than one FileChunk.
        var content = Enumerable.Range(0, 3 * FileTransfer.ChunkSize + 123).Select(i => (byte)(i * 7)).ToArray();
        File.WriteAllBytes(sourcePath, content);
        try
        {
            using var session = new ThumbnailPollerTests.LoopbackSession(
                0, sessionCertificate, masterCertificate, fileInbox: new FileInbox(inboxDirectory));
            var machine = new KnownMachine("Aluno-1", "127.0.0.1", sessionCertificate.Thumbprint, null);

            var first = await Task.Run(() => FileSender.Send(machine, masterCertificate, sourcePath, session.Port))
                .WaitAsync(TimeSpan.FromSeconds(20));
            var second = await Task.Run(() => FileSender.Send(machine, masterCertificate, sourcePath, session.Port))
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal((true, "Lista de exercícios.pdf"), first);
            Assert.Equal((true, "Lista de exercícios (2).pdf"), second);
            Assert.Equal(content, File.ReadAllBytes(Path.Combine(inboxDirectory, "Lista de exercícios.pdf")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(sourcePath)!, recursive: true);
            if (Directory.Exists(inboxDirectory))
            {
                Directory.Delete(inboxDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Send_ToAMachineThatDoesNotAcceptFiles_ReportsWhy()
    {
        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");
        using var sessionCertificate = SelfSignedCertificate.Create("session-under-test");
        var sourcePath = Path.Combine(Path.GetTempPath(), $"midview-source-{Guid.NewGuid():N}.txt");
        File.WriteAllText(sourcePath, "olá");
        try
        {
            using var session = new ThumbnailPollerTests.LoopbackSession(0, sessionCertificate, masterCertificate);
            var machine = new KnownMachine("Aluno-1", "127.0.0.1", sessionCertificate.Thumbprint, null);

            var (saved, detail) = await Task.Run(() => FileSender.Send(machine, masterCertificate, sourcePath, session.Port))
                .WaitAsync(TimeSpan.FromSeconds(20));

            Assert.False(saved);
            Assert.NotEmpty(detail);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }
}
