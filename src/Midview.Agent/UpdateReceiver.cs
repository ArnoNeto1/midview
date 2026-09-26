using System.IO.Compression;
using System.Security.Cryptography;
using Midview.Protocol;

namespace Midview.Agent;

// The running build's identity (PackageFingerprint and label), computed once
// per start in the background: ~160 MB on a slow lab disk shouldn't hold up
// the Agent's start or every status question.
public sealed class InstalledBuild
{
    private readonly Task<(byte[]? Fingerprint, string? Build)> _identity;

    public InstalledBuild(string folder)
    {
        _identity = Task.Run(() => Compute(folder));
    }

    // For tests: an identity known up front.
    public InstalledBuild(byte[]? fingerprint, string? build)
    {
        _identity = Task.FromResult((fingerprint, build));
    }

    public (bool Ready, byte[]? Fingerprint, string? Build) Peek() =>
        _identity.IsCompleted ? (true, _identity.Result.Fingerprint, _identity.Result.Build) : (false, null, null);

    public (byte[]? Fingerprint, string? Build) Wait(TimeSpan timeout) =>
        _identity.Wait(timeout) ? _identity.Result : (null, null);

    // A build without a manifest (installed before builds had one) has no
    // fingerprint: it never counts as "already this version".
    private static (byte[]?, string?) Compute(string folder)
    {
        try
        {
            var build = BuildManifest.Load(folder).Build;
            return (PackageFingerprint.Compute(folder), build);
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Couldn't identify the installed build in {folder}: {ex.Message}");
            return (null, null);
        }
    }
}

// Starts the applier (--apply-update) or the recovery (--recover-update):
// the running build's own exe, as a separate process (so as SYSTEM, like
// the service, and not stopped with it).
public interface IApplierLauncher
{
    int Launch();

    void LaunchRecovery();

    // Returns once the process is gone.
    void Kill(int processId);
}

public sealed class ProcessApplierLauncher : IApplierLauncher
{
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(10);

    private readonly string _agentExe;

    public ProcessApplierLauncher(string agentExe)
    {
        _agentExe = agentExe;
    }

    public int Launch() => Start(UpdateCommands.ApplyArgument);

    public void LaunchRecovery() => Start(UpdateCommands.RecoverArgument);

    public void Kill(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            process.Kill();
            process.WaitForExit(ExitWait);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    private int Start(string argument)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo(_agentExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_agentExe)!,
        };
        startInfo.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("o processo não iniciou");
        return process.Id;
    }
}

// The Agent's side of UpdateOffer (see NetworkUpdate and the design doc):
// one update at a time; receive the zip; answer as soon as it's intact;
// then, without the connection, extract and verify it into its own build
// folder and hand it to the applier. Everything after the answer is
// reported through the update record (GetUpdateStatus).
public sealed class UpdateReceiver
{
    private static readonly TimeSpan FingerprintWait = TimeSpan.FromSeconds(30);

    // Well over the applier's own wait for the lock (UpdateApplier.LockWait)
    // and a slow lab machine's start of a second .NET process.
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(60);
    private const long DiskFloor = 500L * 1024 * 1024;

    private readonly UpdatePaths _paths;
    private readonly string _usbInstallFolder;
    private readonly string _runningAgentExe;
    private readonly InstalledBuild _installed;
    private readonly Func<bool> _shutdownSoon;
    private readonly Func<string, long> _freeBytes;
    private readonly IApplierLauncher _launcher;
    private readonly IUpdateClock _clock;
    private int _busy;

    public UpdateReceiver(
        UpdatePaths paths, string usbInstallFolder, string runningAgentExe, InstalledBuild installed,
        Func<bool> shutdownSoon, Func<string, long> freeBytes, IApplierLauncher launcher, IUpdateClock clock)
    {
        _paths = paths;
        _usbInstallFolder = usbInstallFolder;
        _runningAgentExe = Path.GetFullPath(runningAgentExe);
        _installed = installed;
        _shutdownSoon = shutdownSoon;
        _freeBytes = freeBytes;
        _launcher = launcher;
        _clock = clock;
        MarkInterruptedAttempt();
    }

    public static long FreeBytesOn(string path) =>
        new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace;

    // The whole exchange on one connection: the offer's answer, and after an
    // accept, the chunks and their answer. Returns when the connection is no
    // longer needed; verifying and handing over happen after that, still on
    // this (dedicated) thread.
    public void Handle(byte[] offerPayload, Func<Message> receive, Action<Message> send)
    {
        if (!NetworkUpdate.TryDecodeOffer(offerPayload, out var offer))
        {
            send(Result(false, "pedido inválido"));
            return;
        }

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            send(Result(false, "outra atualização já está em andamento nesta máquina"));
            return;
        }

        UpdateLock? updateLock = null;
        UpdateRecord? record = null;
        var answered = false;
        var attemptId = Convert.ToHexString(offer.AttemptId);
        try
        {
            updateLock = UpdateLock.TryAcquire(_paths.LockPath);
            if (updateLock is null)
            {
                send(Result(false, "outra atualização já está em andamento nesta máquina"));
                return;
            }

            var refusal = CheckOffer(offer, attemptId, out var alreadyInstalled);
            if (alreadyInstalled)
            {
                send(Result(true, "já está nesta versão"));
                return;
            }
            if (refusal is not null)
            {
                send(Result(false, refusal));
                return;
            }

            record = Save(new UpdateRecord(
                attemptId, Convert.ToHexString(offer.Fingerprint), null, UpdateStates.Receiving, "recebendo",
                _clock.Now, null, null));
            answered = true;
            send(new Message(MessageType.UpdateAccepted, Array.Empty<byte>()));

            var problem = ReceiveZip(offer, attemptId, receive);
            if (problem is not null)
            {
                record = TrySave(record with { State = UpdateStates.Failed, Detail = problem });
                DeleteAttemptFiles(attemptId);
                send(Result(false, problem));
                return;
            }
            send(Result(true, "recebida"));

            record = Save(record with { State = UpdateStates.Verifying, Detail = "conferindo o pacote" });
            string build;
            try
            {
                build = ExtractAndVerify(offer, attemptId);
            }
            catch (UpdatePackageException ex)
            {
                record = TrySave(record with { State = UpdateStates.Failed, Detail = ex.Message });
                DeleteAttemptFiles(attemptId);
                return;
            }
            finally
            {
                TryDelete(() => File.Delete(_paths.IncomingZip(attemptId)));
            }

            record = Save(record with
            {
                TargetBuild = build,
                Detail = "iniciando a troca de versão",
                OldBinary = _runningAgentExe,
                NewBinary = Path.Combine(_paths.BuildFolder(attemptId), UpdatePaths.AgentExeName),
            });

            // The applier takes the lock over from here.
            updateLock.Dispose();
            updateLock = null;
            LaunchApplier(record);
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Network update {attemptId} failed: {ex.Message}");
            if (!answered)
            {
                try
                {
                    send(Result(false, $"erro na máquina: {ex.Message}"));
                }
                catch (Exception sendEx) when (sendEx is IOException or ObjectDisposedException)
                {
                    // The Master is gone too.
                }
            }
            if (record is not null && !UpdateStates.IsFinal(record.State))
            {
                TrySave(record with { State = UpdateStates.Failed, Detail = $"interrompida: {ex.Message}" });
                DeleteAttemptFiles(attemptId);
            }
        }
        finally
        {
            updateLock?.Dispose();
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    // What GetUpdateStatus answers. The new build confirms the attempt it's
    // on trial for here -- only for that attempt's id, only while it's
    // provisional, only from the build it switched to -- which is what lets
    // the applier commit. AgentServer has already checked the Master is
    // trusted.
    public UpdateStatusReport Status(byte[] payload)
    {
        var record = UpdateRecordFile.Load(_paths.StatePath);
        if (payload.Length == NetworkUpdate.AttemptIdLength
            && record is { State: UpdateStates.Provisional, NewBinary: { } newBinary }
            && record.AttemptId == Convert.ToHexString(payload)
            && string.Equals(Path.GetFullPath(newBinary), _runningAgentExe, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                UpdateConfirmation.Write(_paths.ConfirmationPath, record.AttemptId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AgentLog.Warn($"Couldn't confirm update {record.AttemptId}: {ex.Message}");
            }
        }

        var (ready, fingerprint, build) = _installed.Peek();
        return new UpdateStatusReport(fingerprint is null ? null : Convert.ToHexString(fingerprint), build, ready, record?.ToReport());
    }

    private string? CheckOffer(UpdateOffer offer, string attemptId, out bool alreadyInstalled)
    {
        alreadyInstalled = false;
        if (offer.ZipSize > NetworkUpdate.MaxZipSize || offer.UncompressedSize > NetworkUpdate.MaxUncompressedSize)
        {
            return "o pacote é grande demais";
        }

        var last = UpdateRecordFile.Load(_paths.StatePath);
        if (last is { State: UpdateStates.Applying or UpdateStates.Provisional or UpdateStates.RollbackPending })
        {
            return "a atualização anterior não terminou; reinicie a máquina para ela ser desfeita e tente de novo";
        }

        if (_shutdownSoon())
        {
            return "a máquina vai desligar em breve; atualize depois";
        }

        var (installed, _) = _installed.Wait(FingerprintWait);
        if (installed is not null && installed.AsSpan().SequenceEqual(offer.Fingerprint))
        {
            alreadyInstalled = true;
            return null;
        }

        try
        {
            _paths.EnsureVersionsRoot();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"a pasta das versões não pôde ser preparada: {ex.Message}";
        }

        CleanOldBuilds(last);

        // Only the builds cleanup keeps (the running one, the one before it)
        // can still have a folder: never extract over one of them.
        if (Directory.Exists(_paths.BuildFolder(attemptId)) || File.Exists(_paths.BuildFolder(attemptId)))
        {
            return "identificador de atualização repetido; tente de novo";
        }

        var needed = offer.ZipSize + offer.UncompressedSize + DiskFloor;
        var free = _freeBytes(_paths.VersionsRoot);
        return free < needed
            ? $"espaço em disco insuficiente (precisa de {needed / (1024 * 1024)} MB livres, tem {free / (1024 * 1024)} MB)"
            : null;
    }

    // Everything under the versions root that isn't the running build, the
    // one it came from, or (for an unfinished attempt) the ones the boot
    // recovery needs -- plus the USB install folder once it's none of those.
    // Best effort: a folder something still runs from is left for next time.
    private void CleanOldBuilds(UpdateRecord? last)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Folder(_runningAgentExe) };
        if (last?.OldBinary is { } oldBinary)
        {
            keep.Add(Folder(oldBinary));
        }
        if (last is { NewBinary: { } newBinary } && !UpdateStates.IsFinal(last.State))
        {
            keep.Add(Folder(newBinary));
        }

        foreach (var directory in Directory.EnumerateDirectories(_paths.VersionsRoot))
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (!keep.Contains(full) && !IsInside(_runningAgentExe, full))
            {
                TryDelete(() => Directory.Delete(full, recursive: true));
            }
        }
        foreach (var zip in Directory.EnumerateFiles(_paths.VersionsRoot, "incoming-*.zip"))
        {
            TryDelete(() => File.Delete(zip));
        }

        var usb = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_usbInstallFolder));
        if (!keep.Contains(usb) && !IsInside(_runningAgentExe, usb)
            && File.Exists(Path.Combine(usb, UpdatePaths.AgentExeName)))
        {
            TryDelete(() => Directory.Delete(usb, recursive: true));
        }
    }

    // A write failure keeps reading and discarding up to the offered size:
    // a refusal sent mid-stream would be lost to the connection's reset.
    private string? ReceiveZip(UpdateOffer offer, string attemptId, Func<Message> receive)
    {
        string? writeProblem = null;
        FileStream? file = null;
        try
        {
            file = new FileStream(_paths.IncomingZip(attemptId), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            writeProblem = $"o pacote não pôde ser gravado: {ex.Message}";
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;
        try
        {
            while (received < offer.ZipSize)
            {
                var message = receive();
                if (message.Type != MessageType.FileChunk || message.Payload.Length == 0
                    || received + message.Payload.Length > offer.ZipSize)
                {
                    return "transferência inválida";
                }

                hash.AppendData(message.Payload);
                received += message.Payload.Length;
                if (file is not null && writeProblem is null)
                {
                    try
                    {
                        file.Write(message.Payload);
                    }
                    catch (IOException ex)
                    {
                        writeProblem = $"o pacote não pôde ser gravado: {ex.Message}";
                    }
                }
            }

            if (file is not null && writeProblem is null)
            {
                file.Flush(flushToDisk: true);
            }
        }
        finally
        {
            file?.Dispose();
        }

        if (writeProblem is not null)
        {
            return writeProblem;
        }
        return hash.GetHashAndReset().AsSpan().SequenceEqual(offer.ZipSha256)
            ? null
            : "o pacote chegou corrompido (o hash não confere)";
    }

    // Into the attempt's own folder, with every entry's path checked and the
    // bytes actually written capped. The result must be exactly the files
    // its manifest lists, the two programs among them, and fingerprint to
    // the offered value. Returns the build's label.
    private string ExtractAndVerify(UpdateOffer offer, string attemptId)
    {
        // New: CheckOffer refused an id whose folder exists, under the lock.
        var folder = _paths.BuildFolder(attemptId);
        Directory.CreateDirectory(folder);
        var folderPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;

        var limit = Math.Min(NetworkUpdate.MaxUncompressedSize, offer.UncompressedSize);
        var entries = new List<PackageFingerprint.FileEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long written = 0;

        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(_paths.IncomingZip(attemptId));
        }
        catch (InvalidDataException)
        {
            throw new UpdatePackageException("o pacote não é um zip válido");
        }

        using (zip)
        {
            if (zip.Entries.Count > NetworkUpdate.MaxEntries)
            {
                throw new UpdatePackageException("o pacote tem arquivos demais");
            }

            var buffer = new byte[81920];
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    continue; // folders are created along with their files
                }
                if (!NetworkUpdate.IsValidRelativePath(entry.FullName, out var relativePath) || !names.Add(relativePath))
                {
                    throw new UpdatePackageException($"caminho inválido ou repetido no pacote: \"{entry.FullName}\"");
                }

                var target = Path.GetFullPath(Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UpdatePackageException($"caminho inválido no pacote: \"{entry.FullName}\"");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long length = 0;
                try
                {
                    using var input = entry.Open();
                    using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        written += read;
                        length += read;
                        if (written > limit)
                        {
                            throw new UpdatePackageException("o pacote é maior do que o anunciado");
                        }
                        output.Write(buffer, 0, read);
                        fileHash.AppendData(buffer, 0, read);
                    }
                    output.Flush(flushToDisk: true);
                }
                catch (InvalidDataException)
                {
                    throw new UpdatePackageException($"o pacote está corrompido em \"{entry.FullName}\"");
                }
                entries.Add(new PackageFingerprint.FileEntry(relativePath, length, fileHash.GetHashAndReset()));
            }
        }

        BuildManifest manifest;
        try
        {
            manifest = BuildManifest.Load(folder);
        }
        catch (FileNotFoundException)
        {
            throw new UpdatePackageException("o pacote é antigo demais para atualizar pela rede (não tem midview-build.json)");
        }
        catch (InvalidDataException ex)
        {
            throw new UpdatePackageException(ex.Message);
        }

        if (manifest.UpdateProtocol < NetworkUpdate.ProtocolVersion)
        {
            throw new UpdatePackageException("o pacote é antigo demais para atualizar pela rede");
        }
        var listed = new HashSet<string>(manifest.Files, StringComparer.OrdinalIgnoreCase);
        if (!listed.SetEquals(names.Where(name => !string.Equals(name, NetworkUpdate.ManifestFileName, StringComparison.OrdinalIgnoreCase))))
        {
            throw new UpdatePackageException("os arquivos do pacote não batem com a lista dele");
        }
        foreach (var required in NetworkUpdate.RequiredFiles)
        {
            if (!listed.Contains(required))
            {
                throw new UpdatePackageException($"o pacote não tem {required}");
            }
        }
        if (!PackageFingerprint.Combine(entries).AsSpan().SequenceEqual(offer.Fingerprint))
        {
            throw new UpdatePackageException("a versão recebida não é a que foi anunciada");
        }

        return manifest.Build;
    }

    // Waits for the applier to take over (it records "applying" once it
    // holds the lock, before it touches the service). One that doesn't in
    // time is ended, and only then is the record read again: still
    // "verifying" means it never switched anything, so the attempt can be
    // failed and its files deleted; anything else means it got further, and
    // the recovery puts the old build back.
    private void LaunchApplier(UpdateRecord record)
    {
        int processId;
        try
        {
            processId = _launcher.Launch();
        }
        catch (Exception ex)
        {
            TrySave(record with { State = UpdateStates.Failed, Detail = $"o atualizador não pôde ser iniciado: {ex.Message}" });
            DeleteAttemptFiles(record.AttemptId);
            return;
        }

        var deadline = _clock.Now + HandshakeTimeout;
        while (_clock.Now < deadline)
        {
            if (TookOver(record))
            {
                return;
            }
            _clock.Sleep(TimeSpan.FromMilliseconds(250));
        }

        _launcher.Kill(processId);
        var current = UpdateRecordFile.Load(_paths.StatePath);
        if (current is { State: UpdateStates.Verifying } && current.AttemptId == record.AttemptId)
        {
            TrySave(record with { State = UpdateStates.Failed, Detail = "o atualizador não começou a tempo" });
            DeleteAttemptFiles(record.AttemptId);
            return;
        }

        AgentLog.Warn($"Network update {record.AttemptId}: the applier was stopped midway (state {current?.State ?? "unreadable"}); starting the recovery.");
        try
        {
            _launcher.LaunchRecovery();
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Couldn't start the update recovery; the boot task will do it: {ex.Message}");
        }
    }

    private bool TookOver(UpdateRecord record) =>
        UpdateRecordFile.Load(_paths.StatePath) is { } current
        && current.AttemptId == record.AttemptId && current.State != UpdateStates.Verifying;

    // A receive or verify cut short by the Agent restarting.
    private void MarkInterruptedAttempt()
    {
        var record = UpdateRecordFile.Load(_paths.StatePath);
        if (record is { State: UpdateStates.Receiving or UpdateStates.Verifying })
        {
            TrySave(record with { State = UpdateStates.Failed, Detail = "interrompida (o Midview reiniciou)" });
        }
    }

    private void DeleteAttemptFiles(string attemptId)
    {
        TryDelete(() => File.Delete(_paths.IncomingZip(attemptId)));
        var folder = _paths.BuildFolder(attemptId);
        if (Directory.Exists(folder) && !IsInside(_runningAgentExe, Path.GetFullPath(folder)))
        {
            TryDelete(() => Directory.Delete(folder, recursive: true));
        }
    }

    // A step forward that can't be recorded stops the attempt (the
    // exception reaches Handle's catch): the applier only ever acts on what
    // the record says.
    private UpdateRecord Save(UpdateRecord record)
    {
        var updated = record with { UpdatedAt = _clock.Now };
        UpdateRecordFile.Save(_paths.StatePath, updated);
        return updated;
    }

    // For failures: best effort.
    private UpdateRecord TrySave(UpdateRecord record)
    {
        try
        {
            return Save(record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Warn($"Couldn't save the update record ({record.State}): {ex.Message}");
            return record with { UpdatedAt = _clock.Now };
        }
    }

    private static Message Result(bool succeeded, string detail) =>
        new(MessageType.CommandAck, CommandResult.Encode(succeeded, detail));

    private static string Folder(string exePath) =>
        Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(exePath))!);

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use or gone; left for the next cleanup.
        }
    }

    private sealed class UpdatePackageException : Exception
    {
        public UpdatePackageException(string message)
            : base(message)
        {
        }
    }
}
