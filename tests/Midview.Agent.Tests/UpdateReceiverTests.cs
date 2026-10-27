using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class UpdateReceiverTests : IDisposable
{
    private static readonly byte[] AttemptBytes = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
    private static readonly string AttemptId = Convert.ToHexString(AttemptBytes);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-receive-{Guid.NewGuid():N}");
    private readonly UpdatePaths _paths;
    private readonly string _runningExe;
    private readonly string _usbFolder;
    private readonly FakeLauncher _launcher;
    private readonly UpdateApplierTests.FakeClock _clock = new();
    private bool _shutdownSoon;
    private long _freeBytes = long.MaxValue;

    public UpdateReceiverTests()
    {
        _paths = new UpdatePaths(Path.Combine(_root, "Midview.update"), Path.Combine(_root, "data"));
        _usbFolder = Path.Combine(_root, "Midview");
        Directory.CreateDirectory(_usbFolder);
        _runningExe = Path.Combine(_usbFolder, UpdatePaths.AgentExeName);
        File.WriteAllText(_runningExe, "running");
        _launcher = new FakeLauncher(_paths);
    }

    // Retried: on Windows, a record the Agent's handler thread is still
    // reading can't be deleted for a moment.
    public void Dispose()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(_root, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 25)
            {
                Thread.Sleep(200);
            }
        }
    }

    private UpdateReceiver Receiver(byte[]? installedFingerprint = null) => new(
        _paths, _usbFolder, _runningExe, new InstalledBuild(installedFingerprint, "2026-09-20 08:00 0000000"),
        () => _shutdownSoon, _ => _freeBytes, _launcher, _clock);

    private UpdateRecord? Record() => UpdateRecordFile.Load(_paths.StatePath);

    [AdministratorFact]
    public void AGoodPackage_IsReceivedVerifiedAndHandedToTheApplier()
    {
        var package = Package.Build(_root, Package.GoodFiles);

        var replies = Exchange(Receiver(), package);

        Assert.Equal(MessageType.UpdateAccepted, replies[0].Type);
        Assert.Equal((true, "recebida"), CommandResult.Decode(replies[1].Payload));
        var record = Record()!;
        Assert.Equal(AttemptId, record.AttemptId);
        Assert.Equal(UpdateStates.Applying, record.State); // taken over by the (fake) applier
        Assert.Equal(package.Label, record.TargetBuild);
        Assert.Equal(_runningExe, record.OldBinary);
        Assert.Equal(Path.Combine(_paths.BuildFolder(AttemptId), UpdatePaths.AgentExeName), record.NewBinary);
        Assert.Equal(1, _launcher.Launches);
        Assert.Equal("agent v2", File.ReadAllText(Path.Combine(_paths.BuildFolder(AttemptId), "Midview.Agent.exe")));
        Assert.Equal(package.Fingerprint, PackageFingerprint.Compute(_paths.BuildFolder(AttemptId)));
        Assert.False(File.Exists(_paths.IncomingZip(AttemptId)));
    }

    [Fact]
    public void TheSameBuild_IsAlreadyInstalled()
    {
        var package = Package.Build(_root, Package.GoodFiles);

        var replies = Exchange(Receiver(installedFingerprint: package.Fingerprint), package, sendChunks: false);

        Assert.Equal((true, "já está nesta versão"), CommandResult.Decode(Assert.Single(replies).Payload));
        Assert.Equal(0, _launcher.Launches);
        Assert.Null(Record());
    }

    [Fact]
    public void AnotherUpdateHoldingTheLock_IsRefused()
    {
        var package = Package.Build(_root, Package.GoodFiles);
        using var held = UpdateLock.TryAcquire(_paths.LockPath);

        var replies = Exchange(Receiver(), package, sendChunks: false);

        Assert.Equal((false, "outra atualização já está em andamento nesta máquina"), CommandResult.Decode(Assert.Single(replies).Payload));
    }

    [Fact]
    public void AShutdownComingUp_IsRefused()
    {
        _shutdownSoon = true;

        var replies = Exchange(Receiver(), Package.Build(_root, Package.GoodFiles), sendChunks: false);

        Assert.Equal((false, "a máquina vai desligar em breve; atualize depois"), CommandResult.Decode(Assert.Single(replies).Payload));
    }

    [Fact]
    public void NotEnoughDisk_IsRefused()
    {
        _freeBytes = 100 * 1024 * 1024;

        var replies = Exchange(Receiver(), Package.Build(_root, Package.GoodFiles), sendChunks: false);

        var (succeeded, detail) = CommandResult.Decode(Assert.Single(replies).Payload);
        Assert.False(succeeded);
        Assert.StartsWith("espaço em disco insuficiente", detail);
    }

    [Theory]
    [InlineData(UpdateStates.Applying)]
    [InlineData(UpdateStates.Provisional)]
    [InlineData(UpdateStates.RollbackPending)]
    public void AnUnfinishedEarlierAttempt_IsRefused(string state)
    {
        UpdateRecordFile.Save(_paths.StatePath, new UpdateRecord("AA", "FP", null, state, "", _clock.Now, _runningExe, _runningExe));

        var replies = Exchange(Receiver(), Package.Build(_root, Package.GoodFiles), sendChunks: false);

        Assert.StartsWith("a atualização anterior não terminou", CommandResult.Decode(Assert.Single(replies).Payload).Detail);
    }

    [AdministratorFact]
    public void ACorruptTransfer_IsRefused_AndLeavesNothing()
    {
        var package = Package.Build(_root, Package.GoodFiles);
        var offer = package.Offer(AttemptBytes);
        var inTransit = package.ZipBytes.ToArray();
        inTransit[^1] ^= 0xFF; // one bit flipped on the way

        var replies = Exchange(Receiver(), offer, inTransit);

        Assert.Equal((false, "o pacote chegou corrompido (o hash não confere)"), CommandResult.Decode(replies[1].Payload));
        Assert.Equal(UpdateStates.Failed, Record()!.State);
        Assert.False(File.Exists(_paths.IncomingZip(AttemptId)));
        Assert.Equal(0, _launcher.Launches);
    }

    // Without administrator rights the package can't be written at all, so
    // this would pass without ever checking that the partial one is removed.
    [AdministratorFact]
    public void AConnectionThatDrops_FailsTheAttempt()
    {
        var package = Package.Build(_root, Package.GoodFiles);
        var receiver = Receiver();
        var sent = new List<Message>();

        receiver.Handle(NetworkUpdate.EncodeOffer(package.Offer(AttemptBytes)),
            () => throw new EndOfStreamException("Connection closed while reading message"), sent.Add);

        Assert.Equal(UpdateStates.Failed, Record()!.State);
        Assert.False(File.Exists(_paths.IncomingZip(AttemptId)));
    }

    [AdministratorTheory]
    [InlineData("../evil.exe")]
    [InlineData("sub/../../evil.exe")]
    [InlineData("C:/Windows/evil.exe")]
    [InlineData("evil.exe:hidden")]
    [InlineData("CON")]
    public void AHostileEntryName_FailsVerification_AndWritesNothingOutside(string entryName)
    {
        var package = Package.Build(_root, Package.GoodFiles, extraEntries: new[] { (entryName, "evil") });

        Exchange(Receiver(), package);

        Assert.Equal(UpdateStates.Failed, Record()!.State);
        Assert.StartsWith("caminho inválido", Record()!.Detail);
        Assert.False(File.Exists(Path.Combine(_paths.VersionsRoot, "evil.exe")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.exe")));
        Assert.False(Directory.Exists(_paths.BuildFolder(AttemptId)));
        Assert.Equal(0, _launcher.Launches);
    }

    [AdministratorFact]
    public void AFileNotInTheManifest_FailsVerification()
    {
        var package = Package.Build(_root, Package.GoodFiles, extraEntries: new[] { ("extra.dll", "not listed") });

        Exchange(Receiver(), package);

        Assert.Equal("os arquivos do pacote não batem com a lista dele", Record()!.Detail);
    }

    [AdministratorFact]
    public void AMissingProgram_FailsVerification()
    {
        var package = Package.Build(_root, new Dictionary<string, string> { ["Midview.Agent.exe"] = "agent v2" });

        Exchange(Receiver(), package);

        Assert.Equal("o pacote não tem Midview.Session.exe", Record()!.Detail);
    }

    [AdministratorFact]
    public void APackageWithoutAManifest_IsTooOld()
    {
        var package = Package.Build(_root, Package.GoodFiles, withManifest: false);

        Exchange(Receiver(), package);

        Assert.StartsWith("o pacote é antigo demais", Record()!.Detail);
    }

    [AdministratorFact]
    public void AnOfferForADifferentBuild_FailsVerification()
    {
        var package = Package.Build(_root, Package.GoodFiles);
        package.Fingerprint[0] ^= 0xFF;

        Exchange(Receiver(), package);

        Assert.Equal("a versão recebida não é a que foi anunciada", Record()!.Detail);
    }

    [AdministratorFact]
    public void MoreBytesThanAnnounced_FailsVerification()
    {
        var package = Package.Build(_root, Package.GoodFiles);
        package.UncompressedSize = 3;

        Exchange(Receiver(), package);

        Assert.Equal("o pacote é maior do que o anunciado", Record()!.Detail);
    }

    [AdministratorFact]
    public void AnApplierThatDoesntTakeOver_IsEnded_AndTheAttemptFails()
    {
        _launcher.TakesOver = false;

        Exchange(Receiver(), Package.Build(_root, Package.GoodFiles));

        Assert.Equal(UpdateStates.Failed, Record()!.State);
        Assert.Equal("o atualizador não começou a tempo", Record()!.Detail);
        Assert.Equal(1, _launcher.Kills);
        Assert.Equal(0, _launcher.Recoveries);
        Assert.False(Directory.Exists(_paths.BuildFolder(AttemptId)));
    }

    [AdministratorFact]
    public void AnApplierThatTookOverAsItWasEnded_IsLeftToTheRecovery_AndItsBuildKept()
    {
        _launcher.TakesOver = false;
        _launcher.TakesOverAsItsKilled = true;

        Exchange(Receiver(), Package.Build(_root, Package.GoodFiles));

        // It may have switched the service already: nothing of it is undone
        // here, the recovery puts the old build back.
        Assert.Equal(UpdateStates.Applying, Record()!.State);
        Assert.Equal(1, _launcher.Recoveries);
        Assert.True(File.Exists(Path.Combine(_paths.BuildFolder(AttemptId), UpdatePaths.AgentExeName)));
    }

    [AdministratorFact]
    public void AnAttemptIdThatNamesAKeptBuild_IsRefused_AndTheBuildLeftAlone()
    {
        // The machine runs the build an earlier attempt with this same id
        // installed.
        var runningFromUpdate = Path.Combine(_paths.BuildFolder(AttemptId), UpdatePaths.AgentExeName);
        Directory.CreateDirectory(Path.GetDirectoryName(runningFromUpdate)!);
        File.WriteAllText(runningFromUpdate, "running");
        var receiver = new UpdateReceiver(
            _paths, _usbFolder, runningFromUpdate, new InstalledBuild(null, null), () => false, _ => long.MaxValue, _launcher, _clock);

        var replies = Exchange(receiver, Package.Build(_root, Package.GoodFiles), sendChunks: false);

        Assert.Equal((false, "identificador de atualização repetido; tente de novo"), CommandResult.Decode(Assert.Single(replies).Payload));
        Assert.Equal("running", File.ReadAllText(runningFromUpdate));
    }

    [Fact]
    public void AStepThatCantBeRecorded_IsRefused_BeforeAnythingIsReceived()
    {
        Directory.CreateDirectory(_paths.StatePath + ".tmp");

        var replies = Exchange(Receiver(), Package.Build(_root, Package.GoodFiles), sendChunks: false);

        var (succeeded, detail) = CommandResult.Decode(Assert.Single(replies).Payload);
        Assert.False(succeeded);
        Assert.StartsWith("erro na máquina", detail);
        Assert.Equal(0, _launcher.Launches);
    }

    [AdministratorFact]
    public void OldBuilds_AreCleanedUp_ButNotTheRunningOrPreviousOne()
    {
        Directory.CreateDirectory(_paths.VersionsRoot);
        var previous = Path.Combine(_paths.VersionsRoot, "PREVIOUS");
        var stale = Path.Combine(_paths.VersionsRoot, "STALE");
        Directory.CreateDirectory(previous);
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(_paths.VersionsRoot, "incoming-OLD.zip"), "x");
        UpdateRecordFile.Save(_paths.StatePath, new UpdateRecord(
            "OLD", "FP", null, UpdateStates.Committed, "", _clock.Now, Path.Combine(previous, UpdatePaths.AgentExeName), _runningExe));

        Exchange(Receiver(), Package.Build(_root, Package.GoodFiles));

        Assert.True(Directory.Exists(previous));
        Assert.False(Directory.Exists(stale));
        Assert.False(File.Exists(Path.Combine(_paths.VersionsRoot, "incoming-OLD.zip")));
        Assert.True(File.Exists(_runningExe)); // the USB folder is the running build here
    }

    [AdministratorFact]
    public void TheUsbFolder_IsCleanedUp_OnceItsNeitherCurrentNorPrevious()
    {
        var runningFromUpdate = Path.Combine(_paths.VersionsRoot, "CURRENT", UpdatePaths.AgentExeName);
        Directory.CreateDirectory(Path.GetDirectoryName(runningFromUpdate)!);
        File.WriteAllText(runningFromUpdate, "running");
        var previous = Path.Combine(_paths.VersionsRoot, "PREVIOUS", UpdatePaths.AgentExeName);
        UpdateRecordFile.Save(_paths.StatePath, new UpdateRecord(
            "OLD", "FP", null, UpdateStates.Committed, "", _clock.Now, previous, runningFromUpdate));
        var receiver = new UpdateReceiver(
            _paths, _usbFolder, runningFromUpdate, new InstalledBuild(null, null), () => false, _ => long.MaxValue, _launcher, _clock);

        Exchange(receiver, Package.Build(_root, Package.GoodFiles));

        Assert.False(Directory.Exists(_usbFolder));
        Assert.True(File.Exists(runningFromUpdate));
    }

    [Fact]
    public void Status_ConfirmsOnlyItsOwnProvisionalAttempt_FromTheNewBuild()
    {
        var newBinary = Path.Combine(_paths.BuildFolder(AttemptId), UpdatePaths.AgentExeName);
        UpdateRecordFile.Save(_paths.StatePath, new UpdateRecord(
            AttemptId, "FP", null, UpdateStates.Provisional, "", _clock.Now, _runningExe, newBinary));
        Directory.CreateDirectory(Path.GetDirectoryName(newBinary)!);

        // The old build answering doesn't confirm, nor does a status without the id.
        Receiver().Status(AttemptBytes);
        Assert.Null(UpdateConfirmation.Read(_paths.ConfirmationPath));

        var newBuild = new UpdateReceiver(
            _paths, _usbFolder, newBinary, new InstalledBuild(null, null), () => false, _ => long.MaxValue, _launcher, _clock);
        newBuild.Status(Array.Empty<byte>());
        newBuild.Status(Enumerable.Repeat((byte)0xEE, 16).ToArray());
        Assert.Null(UpdateConfirmation.Read(_paths.ConfirmationPath));

        var report = newBuild.Status(AttemptBytes);

        Assert.Equal(AttemptId, UpdateConfirmation.Read(_paths.ConfirmationPath));
        Assert.Equal(UpdateStates.Provisional, report.Attempt!.State);
    }

    [Fact]
    public void Status_ReportsTheInstalledBuild()
    {
        var report = Receiver(installedFingerprint: new byte[] { 0xAB, 0xCD }).Status(Array.Empty<byte>());

        Assert.True(report.Ready);
        Assert.Equal("ABCD", report.Fingerprint);
        Assert.Equal("2026-09-20 08:00 0000000", report.Build);
        Assert.Null(report.Attempt);
    }

    [Fact]
    public void AnAttemptCutShortByARestart_IsMarkedFailed()
    {
        UpdateRecordFile.Save(_paths.StatePath, new UpdateRecord("AA", "FP", null, UpdateStates.Receiving, "", _clock.Now, null, null));

        Receiver();

        Assert.Equal(UpdateStates.Failed, Record()!.State);
        Assert.Equal("interrompida (o Midview reiniciou)", Record()!.Detail);
    }

    [AdministratorFact]
    public async Task ThroughTheAgentServer_AnOfferIsReceived_AndTheStatusAnswered()
    {
        using var agentCert = SelfSignedCertificate.Create("agent-under-test");
        using var masterCert = SelfSignedCertificate.Create("master-under-test");
        var siteBlock = new SiteBlockCommandHandler(new SiteBlockState(), Path.GetTempFileName(), "unused-hosts-path", new FakeFirewallController());
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new AgentServer(
            listener, agentCert, new TrustStore(new[] { masterCert.Thumbprint }), siteBlock, updateReceiver: Receiver());
        var package = Package.Build(_root, Package.GoodFiles);
        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));
        try
        {
            var (accepted, received, status) = await Task.Run(() =>
            {
                Message Connect(Func<SecureChannel, Message> exchange)
                {
                    using var tcpClient = new TcpClient();
                    tcpClient.Connect(IPAddress.Loopback, port);
                    using var channel = SecureChannel.AuthenticateAsClient(
                        tcpClient, "agent-under-test", masterCert, new TrustStore(new[] { agentCert.Thumbprint }));
                    return exchange(channel);
                }

                Message? acceptedReply = null;
                var receivedReply = Connect(channel =>
                {
                    channel.Send(new Message(MessageType.UpdateOffer, NetworkUpdate.EncodeOffer(package.Offer(AttemptBytes))));
                    acceptedReply = channel.Receive();
                    foreach (var chunk in package.ZipBytes.Chunk(FileTransfer.ChunkSize))
                    {
                        channel.Send(new Message(MessageType.FileChunk, chunk));
                    }
                    return channel.Receive();
                });
                var statusReply = Connect(channel =>
                {
                    channel.Send(new Message(MessageType.GetUpdateStatus, Array.Empty<byte>()));
                    return channel.Receive();
                });
                return (acceptedReply!, receivedReply, statusReply);
            }).WaitAsync(TimeSpan.FromSeconds(60));

            Assert.Equal(MessageType.UpdateAccepted, accepted.Type);
            Assert.Equal((true, "recebida"), CommandResult.Decode(received.Payload));
            Assert.Equal(MessageType.UpdateStatus, status.Type);
            Assert.True(UpdateStatusReport.TryDecode(status.Payload, out var report));
            Assert.Equal(AttemptId, report.Attempt!.Id);

            // The handler goes on verifying after answering; it's done once
            // the (fake) applier has taken over.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (Record()?.State != UpdateStates.Applying && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
            Assert.Equal(UpdateStates.Applying, Record()?.State);
        }
        finally
        {
            stopServer.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    // One offer and, if accepted, its chunks; returns the Agent's replies.
    private static List<Message> Exchange(UpdateReceiver receiver, Package package, bool sendChunks = true) =>
        Exchange(receiver, package.Offer(AttemptBytes), sendChunks ? package.ZipBytes : Array.Empty<byte>());

    private static List<Message> Exchange(UpdateReceiver receiver, UpdateOffer offer, byte[] zipBytes)
    {
        var sent = new List<Message>();
        var chunks = new Queue<Message>(zipBytes.Chunk(FileTransfer.ChunkSize).Select(chunk => new Message(MessageType.FileChunk, chunk)));
        receiver.Handle(NetworkUpdate.EncodeOffer(offer),
            () => chunks.Count > 0 ? chunks.Dequeue() : throw new EndOfStreamException("no more"), sent.Add);
        return sent;
    }

    // A student-program package the way the Master builds one: a folder with
    // a manifest, zipped, with its fingerprint.
    internal sealed class Package
    {
        public static readonly Dictionary<string, string> GoodFiles = new()
        {
            ["Midview.Agent.exe"] = "agent v2",
            ["Midview.Session.exe"] = "session v2",
            ["Midview.Desk.exe"] = "desk v2",
            ["runtimes/win-x64/native/x.dll"] = "native",
        };

        public string Label { get; private init; } = "2026-09-26 19:40 a1b2c3d";
        public byte[] Fingerprint { get; private init; } = Array.Empty<byte>();
        public byte[] ZipBytes { get; private init; } = Array.Empty<byte>();
        public long UncompressedSize { get; set; }

        public UpdateOffer Offer(byte[] attemptId) =>
            new(attemptId, Fingerprint, ZipBytes.Length, SHA256.HashData(ZipBytes), UncompressedSize);

        public static Package Build(
            string scratch, IReadOnlyDictionary<string, string> files,
            IEnumerable<(string Name, string Content)>? extraEntries = null, bool withManifest = true)
        {
            const string build = "2026-09-26 19:40 a1b2c3d";
            var manifest = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { build, updateProtocol = 1, files = files.Keys }));
            var entries = files.Select(file => (Name: file.Key, Bytes: Encoding.UTF8.GetBytes(file.Value))).ToList();
            if (withManifest)
            {
                entries.Add((NetworkUpdate.ManifestFileName, manifest));
            }
            var fingerprint = PackageFingerprint.Combine(entries.Select(entry =>
                new PackageFingerprint.FileEntry(entry.Name, entry.Bytes.Length, SHA256.HashData(entry.Bytes))));
            entries.AddRange((extraEntries ?? Array.Empty<(string, string)>())
                .Select(entry => (entry.Name, Encoding.UTF8.GetBytes(entry.Content))));

            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, bytes) in entries)
                {
                    using var stream = zip.CreateEntry(name).Open();
                    stream.Write(bytes);
                }
            }

            return new Package
            {
                Label = build,
                Fingerprint = fingerprint,
                ZipBytes = memory.ToArray(),
                UncompressedSize = entries.Sum(entry => (long)entry.Bytes.Length),
            };
        }
    }

    // Stands in for launching Midview.Agent.exe --apply-update: by default
    // it takes over the way the real applier does, by recording "applying".
    private sealed class FakeLauncher : IApplierLauncher
    {
        private readonly UpdatePaths _paths;

        public FakeLauncher(UpdatePaths paths)
        {
            _paths = paths;
        }

        public bool TakesOver { get; set; } = true;

        // An applier that got going just as the receiver gave up on it.
        public bool TakesOverAsItsKilled { get; set; }

        public int Launches { get; private set; }
        public int Kills { get; private set; }
        public int Recoveries { get; private set; }

        public int Launch()
        {
            Launches++;
            if (TakesOver)
            {
                TakeOver();
            }
            return 4242;
        }

        public void LaunchRecovery() => Recoveries++;

        public void Kill(int processId)
        {
            Kills++;
            if (TakesOverAsItsKilled)
            {
                TakeOver();
            }
        }

        private void TakeOver()
        {
            var record = UpdateRecordFile.Load(_paths.StatePath)!;
            UpdateRecordFile.Save(_paths.StatePath, record with { State = UpdateStates.Applying });
        }
    }
}
