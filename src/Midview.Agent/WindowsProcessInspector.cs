using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Midview.Protocol;

namespace Midview.Agent;

// The Win32 half of program blocking, run by the Agent (SYSTEM). Everything
// that can fail -- a process that ended meanwhile, one that can't be opened
// -- makes that process be left alone (or undecided, to be looked at again),
// never an exception out of here.
public sealed class WindowsProcessInspector : IProcessInspector
{
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const int TokenUserClass = 1;
    private const int TokenGroupsClass = 2;
    private const int TokenElevationClass = 20;
    private const uint SeGroupEnabled = 0x4;
    private const uint SeGroupUseForDenyOnly = 0x10;
    private const uint SePrivilegeEnabled = 0x2;
    private const uint ReadControl = 0x00020000;
    private const uint ShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int SeFileObject = 1;
    private const uint OwnerSecurityInformation = 1;
    private const uint DriveRemote = 4;
    private const uint MbIconInformation = 0x40;
    private const uint MbSetForeground = 0x10000;
    private const int NoticeSeconds = 10;

    // A session's user is looked at again soon: a logoff and a logon by
    // someone else must not leave the old user's name in charge.
    private static readonly TimeSpan SessionUserLifetime = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SessionUserRecheck = TimeSpan.FromSeconds(1);

    // Who may own a file that Windows or an administrator installed: SYSTEM,
    // Administrators and TrustedInstaller.
    private static readonly SecurityIdentifier AdministratorsSid = new("S-1-5-32-544");

    private static readonly SecurityIdentifier[] TrustedOwners =
    {
        new("S-1-5-18"),
        new("S-1-5-32-544"),
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"),
    };

    private readonly string _windowsFolder = FolderPrefix(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    // Places a standard user can't write (the Windows folder has a few
    // exceptions, which the owner check covers).
    private readonly string[] _trustedFolders =
    {
        FolderPrefix(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),
        FolderPrefix(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
        FolderPrefix(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)),
        FolderPrefix(AppContext.BaseDirectory),
    };

    private readonly object _gate = new();
    private readonly SemaphoreSlim _noticeShowing = new(1, 1);
    private readonly Dictionary<int, (SecurityIdentifier User, DateTime ReadAt)> _sessionUsers = new();
    private readonly Dictionary<(string Path, long Length, DateTime Written), (string? Original, string? Description, SecurityIdentifier? Owner)> _files = new();

    public WindowsProcessInspector()
    {
        EnableBackupPrivilege();
    }

    public IReadOnlyList<RunningProcess> ListInteractive()
    {
        var running = new List<RunningProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.SessionId == 0 || process.Id == Environment.ProcessId)
                {
                    continue;
                }
                using var handle = Open(process.Id, ProcessQueryLimitedInformation);
                if (handle is not null && CreationTime(handle) is { } created)
                {
                    running.Add(new RunningProcess(process.Id, created, process.SessionId));
                }
            }
        }
        // Oldest first: a program is judged before what it starts.
        running.Sort((a, b) => a.CreationTime.CompareTo(b.CreationTime));
        return running;
    }

    public ProcessFacts? Inspect(RunningProcess process)
    {
        using var handle = Open(process.ProcessId, ProcessQueryLimitedInformation);
        if (handle is null || CreationTime(handle) != process.CreationTime || ImagePath(handle) is not { } path)
        {
            return null;
        }

        var (owner, elevated) = TokenFacts(handle);
        var sessionUser = SessionUser(process.SessionId, owner);
        if (sessionUser is null)
        {
            // Nobody is logged on there (yet): can't tell whose it is.
            return null;
        }

        var fileName = Path.GetFileName(path);
        var imageName = AppBlockPolicy.ProgramName(fileName) ?? fileName.ToLowerInvariant();
        var inWindows = path.StartsWith(_windowsFolder, StringComparison.OrdinalIgnoreCase);
        var inTrusted = _trustedFolders.Any(folder => folder.Length > 1 && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
        var ownedBySessionUser = owner is not null && owner == sessionUser;

        // Only the logged-on user's own, non-elevated processes can be ended:
        // for the rest nothing about the file matters.
        var (original, description, fileOwner) = ownedBySessionUser && !elevated
            ? FileFacts(path)
            : (null, null, null);

        return new ProcessFacts(
            process.ProcessId,
            DateTime.FromFileTimeUtc(process.CreationTime),
            process.SessionId,
            OwnedBySessionUser: ownedBySessionUser,
            IsElevated: elevated,
            ImageName: imageName,
            OriginalFilename: AppBlockPolicy.ProgramName(original),
            InWindowsFolder: inWindows,
            InTrustedFolder: inTrusted,
            FileOwner: KindOf(fileOwner, sessionUser),
            DisplayName: OpenProgramsReport.CleanDisplayName(description, imageName));
    }

    public bool End(RunningProcess process)
    {
        using var handle = Open(process.ProcessId, ProcessTerminate | ProcessQueryLimitedInformation);
        return handle is not null && CreationTime(handle) == process.CreationTime && TerminateProcess(handle, 1);
    }

    // The message box stays up only as long as the call waits for it (the
    // timeout is ignored when it doesn't), so the call waits -- on a thread of
    // its own, so the enforcer isn't held up -- and a notice is skipped while
    // another is still on the screen: boxes can't pile up.
    public void Notify(int sessionId, string message)
    {
        if (!_noticeShowing.Wait(0))
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                const string title = "Midview";
                WTSSendMessage(IntPtr.Zero, sessionId, title, title.Length * 2, message, message.Length * 2,
                    MbIconInformation | MbSetForeground, NoticeSeconds, out _, true);
            }
            catch (Exception ex)
            {
                AgentLog.Warn($"Couldn't tell the student a program was blocked: {ex.Message}");
            }
            finally
            {
                _noticeShowing.Release();
            }
        });
    }

    private static string FolderPrefix(string folder) =>
        string.IsNullOrEmpty(folder) ? string.Empty : Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;

    private static FileOwnerKind KindOf(SecurityIdentifier? fileOwner, SecurityIdentifier sessionUser)
    {
        if (fileOwner is null)
        {
            return FileOwnerKind.Other;
        }
        if (TrustedOwners.Contains(fileOwner))
        {
            return FileOwnerKind.Trusted;
        }
        return fileOwner == sessionUser ? FileOwnerKind.Student : FileOwnerKind.Other;
    }

    private static SafeProcessHandle? Open(int processId, uint access)
    {
        var handle = OpenProcess(access, false, processId);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return handle;
    }

    private static long? CreationTime(SafeProcessHandle handle) =>
        GetProcessTimes(handle, out var created, out _, out _, out _) ? created : null;

    private static string? ImagePath(SafeProcessHandle handle)
    {
        var buffer = new StringBuilder(1024);
        var size = buffer.Capacity;
        return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
    }

    private static (SecurityIdentifier? Owner, bool Elevated) TokenFacts(SafeProcessHandle handle)
    {
        if (!OpenProcessToken(handle, TokenQuery, out var token))
        {
            return (null, false);
        }
        using (token)
        {
            // Elevated means holding administrator rights: the token says it is
            // elevated AND the Administrators group is enabled in it. (A token
            // restricted from an elevated one, like a SAFER "Basic User" token,
            // can carry the elevated flag with that group turned to deny-only:
            // it holds no administrator rights.)
            return (TokenUser(token), TokenElevated(token) && AdministratorsEnabled(token));
        }
    }

    // The user logged on to a session. A failure is never kept (nobody may
    // be logged on yet); a success only for a moment, and it is read again
    // sooner when a process turns out to belong to someone else.
    private SecurityIdentifier? SessionUser(int sessionId, SecurityIdentifier? processOwner)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (_sessionUsers.TryGetValue(sessionId, out var cached))
            {
                var age = now - cached.ReadAt;
                if (age < SessionUserLifetime && (age < SessionUserRecheck || processOwner is null || processOwner == cached.User))
                {
                    return cached.User;
                }
            }

            SecurityIdentifier? user = null;
            if (WTSQueryUserToken(sessionId, out var token))
            {
                using (token)
                {
                    user = TokenUser(token);
                }
            }

            if (user is null)
            {
                _sessionUsers.Remove(sessionId);
                return null;
            }
            _sessionUsers[sessionId] = (user, now);
            return user;
        }
    }

    // Version information and owner of an image file, kept per path, size
    // and write time. Nothing is read from a network path: a share that
    // doesn't answer must not hold up the Agent.
    private (string? Original, string? Description, SecurityIdentifier? Owner) FileFacts(string path)
    {
        if (IsNetworkPath(path))
        {
            return (null, null, null);
        }

        try
        {
            var file = new FileInfo(path);
            var key = (path, file.Length, file.LastWriteTimeUtc);
            lock (_gate)
            {
                if (_files.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            string? original = null, description = null;
            try
            {
                var version = FileVersionInfo.GetVersionInfo(path);
                original = version.OriginalFilename;
                description = version.FileDescription;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // No version information: matched by its file name only.
            }
            var owner = ReadOwner(path);

            lock (_gate)
            {
                if (_files.Count > 4096)
                {
                    _files.Clear();
                }
                _files[key] = (original, description, owner);
            }
            return (original, description, owner);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, null, null);
        }
    }

    private static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && GetDriveType(root) == DriveRemote;
    }

    // The file's owner, read with the backup privilege so that a file whose
    // permissions don't let SYSTEM read them still tells who owns it. Null
    // when it can't be read (a FAT/exFAT volume has no owner): such a file
    // is treated as not installed by anyone trusted.
    private static SecurityIdentifier? ReadOwner(string path)
    {
        using var file = CreateFile(path, ReadControl, ShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (file.IsInvalid)
        {
            return null;
        }

        var descriptor = IntPtr.Zero;
        try
        {
            var result = GetSecurityInfo(file, SeFileObject, OwnerSecurityInformation, out var owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out descriptor);
            return result == 0 && owner != IntPtr.Zero ? new SecurityIdentifier(owner) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        finally
        {
            if (descriptor != IntPtr.Zero)
            {
                LocalFree(descriptor);
            }
        }
    }

    private static void EnableBackupPrivilege()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            if (!OpenProcessToken(self.SafeHandle, TokenAdjustPrivileges | TokenQuery, out var token))
            {
                return;
            }
            using (token)
            {
                if (LookupPrivilegeValue(null, "SeBackupPrivilege", out var luid))
                {
                    var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = SePrivilegeEnabled };
                    AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Without it, owners are read the ordinary way.
        }
    }

    private static SecurityIdentifier? TokenUser(SafeAccessTokenHandle token)
    {
        GetTokenInformation(token, TokenUserClass, IntPtr.Zero, 0, out var needed);
        if (needed == 0)
        {
            return null;
        }
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!GetTokenInformation(token, TokenUserClass, buffer, needed, out _))
            {
                return null;
            }
            // TOKEN_USER starts with a SID_AND_ATTRIBUTES whose first field
            // is the SID pointer.
            return new SecurityIdentifier(Marshal.ReadIntPtr(buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TokenElevated(SafeAccessTokenHandle token)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            return GetTokenInformation(token, TokenElevationClass, buffer, sizeof(int), out _) && Marshal.ReadInt32(buffer) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool AdministratorsEnabled(SafeAccessTokenHandle token)
    {
        GetTokenInformation(token, TokenGroupsClass, IntPtr.Zero, 0, out var needed);
        if (needed == 0)
        {
            return false;
        }
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!GetTokenInformation(token, TokenGroupsClass, buffer, needed, out _))
            {
                // Can't tell: treated as holding the rights, the side that ends nothing.
                return true;
            }

            // TOKEN_GROUPS: a count, then SID_AND_ATTRIBUTES entries (a SID
            // pointer, then a uint of attributes), laid out for the pointer size.
            var count = Marshal.ReadInt32(buffer);
            var entrySize = IntPtr.Size * 2;
            for (var i = 0; i < count; i++)
            {
                var entry = buffer + IntPtr.Size + i * entrySize;
                var attributes = unchecked((uint)Marshal.ReadInt32(entry + IntPtr.Size));
                if ((attributes & SeGroupEnabled) != 0 && (attributes & SeGroupUseForDenyOnly) == 0
                    && new SecurityIdentifier(Marshal.ReadIntPtr(entry)) == AdministratorsSid)
                {
                    return true;
                }
            }
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // One privilege (the LUID is two uint fields, read here as one long).
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDriveTypeW")]
    private static extern uint GetDriveType(string rootPath);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityInfo(
        SafeFileHandle handle, int objectType, uint information, out IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl, out IntPtr descriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle token, int informationClass, IntPtr information, int length, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW")]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        SafeAccessTokenHandle token, bool disableAll, ref TokenPrivileges newState, int bufferLength, IntPtr previous, IntPtr returnLength);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(int sessionId, out SafeAccessTokenHandle token);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSSendMessageW")]
    private static extern bool WTSSendMessage(
        IntPtr server, int sessionId, string title, int titleLength, string message, int messageLength,
        uint style, int timeoutSeconds, out int response, bool wait);
}
