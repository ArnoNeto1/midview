using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Midview.Protocol;

namespace Midview.Agent;

// Where the pieces of a network update live. VersionsRoot is always under
// Program Files (students can't create folders there); the record, the
// confirmation and the lock are in a folder of the Agent's data directory
// that students can't even open (see EnsureStateDirectory).
public sealed record UpdatePaths(string VersionsRoot, string DataDirectory)
{
    public const string ServiceName = "MidviewAgent";
    public const string RecoveryTaskName = "Midview - Recuperar atualização";
    public const string AgentExeName = "Midview.Agent.exe";

    public static UpdatePaths Default => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Midview.update"),
        AgentDataDirectory.DirectoryPath);

    // Where a USB install puts the program: an old build to clean up once
    // it's neither the current nor the previous one.
    public static string UsbInstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Midview");

    public string StateDirectory => Path.Combine(DataDirectory, "update");
    public string StatePath => Path.Combine(StateDirectory, "update-state.json");
    public string ConfirmationPath => Path.Combine(StateDirectory, "update-confirmed.json");
    public string LockPath => Path.Combine(StateDirectory, "update.lock");

    public string BuildFolder(string attemptId) => Path.Combine(VersionsRoot, attemptId);
    public string IncomingZip(string attemptId) => Path.Combine(VersionsRoot, $"incoming-{attemptId}.zip");

    // SYSTEM and Administrators only, set at creation and again on every
    // start. The data directory lets Users read what's in it (the Session
    // needs that), and a student who can open a file for reading can also
    // hold it open without sharing: the lock would never be free again, and
    // the record could never be replaced. Here they can't open anything.
    public void EnsureStateDirectory()
    {
        var directory = new DirectoryInfo(StateDirectory);
        if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException($"{StateDirectory} é um atalho, não uma pasta");
        }
        if (!OperatingSystem.IsWindows())
        {
            directory.Create();
            return;
        }

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                AccessControlType.Allow));
        }
        if (directory.Exists)
        {
            directory.SetAccessControl(security);
        }
        else
        {
            directory.Create(security);
        }
    }

    // Created with its permissions set at creation (SYSTEM and Administrators
    // full, Users read and execute -- the Session runs from here as the
    // student), so there's never a moment a student could write into it.
    // One that already exists as a link, or owned by anyone else, is refused.
    public void EnsureVersionsRoot()
    {
        var root = new DirectoryInfo(VersionsRoot);
        if (root.Exists)
        {
            if (root.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException($"{VersionsRoot} é um atalho, não uma pasta");
            }
            if (OperatingSystem.IsWindows())
            {
                var owner = root.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                if (owner is null || !(owner.IsWellKnown(WellKnownSidType.LocalSystemSid)
                                       || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)))
                {
                    throw new UnauthorizedAccessException($"{VersionsRoot} não pertence ao sistema nem aos administradores");
                }
            }
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            root.Create();
            return;
        }

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var (sid, rights) in new[]
                 {
                     (WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl),
                     (WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl),
                     (WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute),
                 })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null), rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                AccessControlType.Allow));
        }
        root.Create(security);
    }
}

// The one record of the last update attempt: written by the receiver while
// receiving and verifying, then by the applier (or the boot recovery), and
// reported by GetUpdateStatus. Its JSON is a contract between versions: the
// applier reading it is the build being replaced. OldBinary and NewBinary
// are full paths to Midview.Agent.exe in the build switched from and to.
public sealed record UpdateRecord(
    [property: JsonPropertyName("attemptId")] string AttemptId,
    [property: JsonPropertyName("targetFingerprint")] string TargetFingerprint,
    [property: JsonPropertyName("targetBuild")] string? TargetBuild,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("oldBinary")] string? OldBinary,
    [property: JsonPropertyName("newBinary")] string? NewBinary)
{
    public UpdateAttemptReport ToReport() => new(AttemptId, State, Detail, UpdatedAt);
}

public static class UpdateRecordFile
{
    private const int MoveAttempts = 20;
    private static readonly TimeSpan MoveRetryDelay = TimeSpan.FromMilliseconds(100);

    // Missing or unreadable: no attempt to report.
    public static UpdateRecord? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<UpdateRecord>(ReadShared(path)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Temp file, flushed to disk, then swapped in: a power cut leaves the
    // old record or the new one, never half of one -- the boot recovery
    // depends on it. Throws if it couldn't be written: callers decide what
    // an unsaved state means.
    public static void Save(string path, UpdateRecord record) => WriteDurably(path, JsonSerializer.SerializeToUtf8Bytes(record));

    // Windows won't swap a file in while another handle has it open, even
    // one shared for delete: the retries (2 s in all) wait out a status
    // question reading it, or an antivirus scan.
    internal static void WriteDurably(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < MoveAttempts)
            {
                Thread.Sleep(MoveRetryDelay);
            }
        }
    }

    internal static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

// Written by the new build when a trusted Master asks for the status of the
// attempt it's on trial for: the only thing that lets the applier commit.
public static class UpdateConfirmation
{
    public static void Write(string path, string attemptId) =>
        UpdateRecordFile.WriteDurably(path, JsonSerializer.SerializeToUtf8Bytes(new { attemptId }));

    public static string? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            using var document = JsonDocument.Parse(UpdateRecordFile.ReadShared(path));
            return document.RootElement.TryGetProperty("attemptId", out var id) ? id.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale confirmation names an old attempt id, which never matches.
        }
    }
}

// One update at a time, across processes: the receiver, the applier and the
// installer scripts all take ProgramData\Midview\update\update.lock with
// FileShare.None. Windows releases it if the holder dies, and students
// can't create or hold it (they can't open anything in that folder).
public sealed class UpdateLock : IDisposable
{
    private readonly FileStream _stream;

    private UpdateLock(FileStream stream)
    {
        _stream = stream;
    }

    public static UpdateLock? TryAcquire(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new UpdateLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static UpdateLock? Acquire(string path, TimeSpan wait, IUpdateClock clock)
    {
        var deadline = clock.Now + wait;
        while (true)
        {
            var acquired = TryAcquire(path);
            if (acquired is not null || clock.Now >= deadline)
            {
                return acquired;
            }
            clock.Sleep(TimeSpan.FromMilliseconds(500));
        }
    }

    public void Dispose() => _stream.Dispose();
}

public interface IUpdateClock
{
    DateTimeOffset Now { get; }

    void Sleep(TimeSpan duration);
}

public sealed class SystemUpdateClock : IUpdateClock
{
    public static readonly SystemUpdateClock Instance = new();

    public DateTimeOffset Now => DateTimeOffset.Now;

    public void Sleep(TimeSpan duration) => Thread.Sleep(duration);
}
