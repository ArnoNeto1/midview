using System.ComponentModel;
using System.Security.Cryptography;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class ProgramInstallerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"midview-install-{Guid.NewGuid():N}");
    private readonly List<(string FileName, string Arguments, byte[]? Installer)> _runs = new();
    private long _freeBytes = long.MaxValue;
    private Func<int?> _exitCode = () => 0;

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private ProgramInstaller Installer() => new(_folder, _ => _freeBytes, (fileName, arguments, _) =>
    {
        // What the installer would find on disk when it starts.
        var installer = File.Exists(fileName) ? File.ReadAllBytes(fileName)
            : arguments.Split('"') is [_, var msi, ..] && File.Exists(msi) ? File.ReadAllBytes(msi)
            : null;
        _runs.Add((fileName, arguments, installer));
        return _exitCode();
    });

    private static List<Message> Exchange(ProgramInstaller installer, InstallRequest request, byte[] file)
    {
        var sent = new List<Message>();
        var chunks = new Queue<Message>(file.Chunk(FileTransfer.ChunkSize).Select(chunk => new Message(MessageType.FileChunk, chunk)));
        installer.Handle(ProgramInstall.EncodeRequest(request),
            () => chunks.Count > 0 ? chunks.Dequeue() : throw new EndOfStreamException("no more"), sent.Add);
        return sent;
    }

    private static (bool Succeeded, string Detail) Last(List<Message> sent)
    {
        Assert.Equal(MessageType.CommandAck, sent[^1].Type);
        return CommandResult.Decode(sent[^1].Payload);
    }

    [Fact]
    public void AnExeInstaller_IsReceivedWhole_AndRunWithTheTeachersArguments()
    {
        var file = RandomNumberGenerator.GetBytes(FileTransfer.ChunkSize * 2 + 77);

        var sent = Exchange(Installer(), new InstallRequest("Setup Scratch.exe", file.Length, "/VERYSILENT /NORESTART"), file);

        Assert.Equal(new[] { MessageType.InstallAccepted, MessageType.InstallStarted, MessageType.CommandAck }, sent.Select(m => m.Type));
        Assert.Equal((true, "instalado"), Last(sent));
        var run = Assert.Single(_runs);
        Assert.Equal("Setup Scratch.exe", Path.GetFileName(run.FileName));
        Assert.StartsWith(_folder, run.FileName);
        Assert.Equal("/VERYSILENT /NORESTART", run.Arguments);
        Assert.Equal(file, run.Installer);
    }

    [Fact]
    public void AnMsi_GoesThroughMsiexec_SilentAndWithoutRestarting()
    {
        var file = RandomNumberGenerator.GetBytes(1000);

        var sent = Exchange(Installer(), new InstallRequest("geogebra.msi", file.Length, "ALLUSERS=1"), file);

        Assert.True(Last(sent).Succeeded);
        var run = Assert.Single(_runs);
        Assert.Equal("msiexec.exe", run.FileName);
        Assert.Matches("""^/i ".*\\geogebra\.msi" /qn /norestart ALLUSERS=1$""", run.Arguments);
        Assert.Equal(file, run.Installer);
    }

    [Fact]
    public void AnMsi_WithoutArguments_EndsWithItsOwnSwitches()
    {
        var (fileName, arguments) = ProgramInstaller.CommandFor(@"C:\x\a.MSI", "");

        Assert.Equal("msiexec.exe", fileName);
        Assert.Equal("""/i "C:\x\a.MSI" /qn /norestart""", arguments);
    }

    [Fact]
    public void TheInstallersFolder_IsGoneAfterwards()
    {
        Exchange(Installer(), new InstallRequest("setup.exe", 10, "/S"), new byte[10]);

        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Theory]
    [InlineData(3010, true, "instalado; reinicie a máquina para concluir")]
    [InlineData(1641, true, "instalado; reinicie a máquina para concluir")]
    [InlineData(1602, false, "a instalação foi cancelada")]
    [InlineData(1618, false, "outra instalação do Windows está em andamento nesta máquina; tente de novo daqui a pouco")]
    [InlineData(2, false, "o instalador terminou com o código 2")]
    public void TheExitCode_SaysHowItWent(int exitCode, bool succeeded, string detail)
    {
        _exitCode = () => exitCode;

        var sent = Exchange(Installer(), new InstallRequest("setup.exe", 10, "/S"), new byte[10]);

        Assert.Equal((succeeded, detail), Last(sent));
    }

    [Fact]
    public void AnInstallerThatNeverEnds_IsReportedAsStuck()
    {
        _exitCode = () => null;

        var sent = Exchange(Installer(), new InstallRequest("setup.exe", 10, ""), new byte[10]);

        var (succeeded, detail) = Last(sent);
        Assert.False(succeeded);
        Assert.Contains("não terminou em 30 minutos", detail);
    }

    [Fact]
    public void AnInstallerThatCantStart_SaysWhy()
    {
        _exitCode = () => throw new Win32Exception(193);

        var sent = Exchange(Installer(), new InstallRequest("setup.exe", 10, ""), new byte[10]);

        var (succeeded, detail) = Last(sent);
        Assert.False(succeeded);
        Assert.StartsWith("o instalador não pôde ser iniciado: ", detail);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public void WithoutRoomOnTheDisk_ItsRefusedBeforeAnythingIsSent()
    {
        _freeBytes = 600L * 1024 * 1024;

        var sent = Exchange(Installer(), new InstallRequest("setup.exe", 200L * 1024 * 1024, ""), Array.Empty<byte>());

        var message = Assert.Single(sent);
        var (succeeded, detail) = CommandResult.Decode(message.Payload);
        Assert.False(succeeded);
        Assert.StartsWith("pouco espaço em disco nesta máquina", detail);
        Assert.Empty(_runs);
    }

    [Fact]
    public void AnythingButTheFile_EndsTheTransfer_AndNothingRuns()
    {
        var sent = new List<Message>();
        Installer().Handle(ProgramInstall.EncodeRequest(new InstallRequest("setup.exe", 10, "")),
            () => new Message(MessageType.Heartbeat, Array.Empty<byte>()), sent.Add);

        Assert.Equal((false, "transferência inválida"), Last(sent));
        Assert.Empty(_runs);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public void AnInvalidRequest_IsRefused()
    {
        var sent = new List<Message>();

        Installer().Handle(new byte[] { 1, 2, 3 }, () => throw new EndOfStreamException(), sent.Add);

        Assert.Equal((false, "pedido inválido"), Last(sent));
    }

    [Fact]
    public async Task OneInstallAtATime()
    {
        using var running = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var installer = new ProgramInstaller(_folder, _ => long.MaxValue, (_, _, _) =>
        {
            running.Set();
            release.Wait(TimeSpan.FromSeconds(20));
            return 0;
        });
        var first = Task.Run(() => Exchange(installer, new InstallRequest("a.exe", 10, ""), new byte[10]));
        Assert.True(running.Wait(TimeSpan.FromSeconds(20)));

        var second = Exchange(installer, new InstallRequest("b.exe", 10, ""), new byte[10]);
        release.Set();

        Assert.Equal((false, "outra instalação já está em andamento nesta máquina"), Last(second));
        Assert.True(Last(await first.WaitAsync(TimeSpan.FromSeconds(20))).Succeeded);
    }

    [Fact]
    public void LeftoversFromAnEarlierStart_AreDeleted()
    {
        var leftover = Path.Combine(_folder, "0123abcd");
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "setup.exe"), "old");

        Installer();

        Assert.False(Directory.Exists(leftover));
    }
}
