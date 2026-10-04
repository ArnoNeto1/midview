using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Midview.Protocol;

namespace Midview.Agent;

// The Win32 half of program blocking, run by the Agent (SYSTEM). Everything
// that can fail -- a process that ended meanwhile, one that can't be opened
// -- makes that process be left alone, never an exception out of here.
public sealed class WindowsProcessInspector : IProcessInspector
{
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenUserClass = 1;
    private const int TokenElevationClass = 20;
    private const uint MbIconInformation = 0x40;
    private const uint MbSetForeground = 0x10000;
    private const int NoticeSeconds = 10;

    private static readonly TimeSpan SessionUserLifetime = TimeSpan.FromMinutes(1);

    private readonly string _windowsFolder =
        Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) + Path.DirectorySeparatorChar;

    private readonly Dictionary<int, (SecurityIdentifier? User, DateTime Until)> _sessionUsers = new();
    private readonly Dictionary<(string Path, long Length, DateTime Written), (string? Original, string? Description, SecurityIdentifier? Owner)> _files = new();

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
        var sessionUser = SessionUser(process.SessionId);
        var fileName = Path.GetFileName(path);
        var imageName = AppBlockPolicy.NormalizeExeName(fileName) ?? fileName.ToLowerInvariant();
        var (original, description, fileOwner) = FileFacts(path);

        return new ProcessFacts(
            process.ProcessId,
            DateTime.FromFileTimeUtc(process.CreationTime),
            process.SessionId,
            OwnedBySessionUser: owner is not null && sessionUser is not null && owner == sessionUser,
            IsElevated: elevated,
            ImageName: imageName,
            OriginalFilename: AppBlockPolicy.NormalizeExeName(original),
            InWindowsFolder: path.StartsWith(_windowsFolder, StringComparison.OrdinalIgnoreCase),
            FileOwner: fileOwner is null ? FileOwnerKind.Unknown
                : sessionUser is not null && fileOwner == sessionUser ? FileOwnerKind.Student
                : FileOwnerKind.Installed,
            DisplayName: OpenProgramsReport.CleanDisplayName(description, imageName));
    }

    public bool End(RunningProcess process)
    {
        using var handle = Open(process.ProcessId, ProcessTerminate | ProcessQueryLimitedInformation);
        return handle is not null && CreationTime(handle) == process.CreationTime && TerminateProcess(handle, 1);
    }

    public void Notify(int sessionId, string message)
    {
        const string title = "Midview";
        WTSSendMessage(IntPtr.Zero, sessionId, title, title.Length * 2, message, message.Length * 2,
            MbIconInformation | MbSetForeground, NoticeSeconds, out _, false);
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
            return (TokenUser(token), TokenElevated(token));
        }
    }

    private SecurityIdentifier? SessionUser(int sessionId)
    {
        var now = DateTime.UtcNow;
        if (_sessionUsers.TryGetValue(sessionId, out var cached) && cached.Until > now)
        {
            return cached.User;
        }

        SecurityIdentifier? user = null;
        if (WTSQueryUserToken(sessionId, out var token))
        {
            using (token)
            {
                user = TokenUser(token);
            }
        }
        _sessionUsers[sessionId] = (user, now + SessionUserLifetime);
        return user;
    }

    private (string? Original, string? Description, SecurityIdentifier? Owner) FileFacts(string path)
    {
        try
        {
            var file = new FileInfo(path);
            var key = (path, file.Length, file.LastWriteTimeUtc);
            if (_files.TryGetValue(key, out var cached))
            {
                return cached;
            }

            string? original = null, description = null;
            SecurityIdentifier? owner = null;
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
            try
            {
                owner = file.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            }
            catch (SystemException)
            {
                // Unknown owner: treated as not the student's.
            }

            if (_files.Count > 4096)
            {
                _files.Clear();
            }
            _files[key] = (original, description, owner);
            return (original, description, owner);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, null, null);
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle token, int informationClass, IntPtr information, int length, out int returnLength);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(int sessionId, out SafeAccessTokenHandle token);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSSendMessageW")]
    private static extern bool WTSSendMessage(
        IntPtr server, int sessionId, string title, int titleLength, string message, int messageLength,
        uint style, int timeoutSeconds, out int response, bool wait);
}
