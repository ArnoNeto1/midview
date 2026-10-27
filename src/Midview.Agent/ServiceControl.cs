using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Midview.Agent;

public enum ServiceRunState
{
    Stopped,
    StartPending,
    StopPending,
    Running,
    Other,
}

public readonly record struct ServiceSnapshot(ServiceRunState State, int ProcessId);

// The service operations a network update needs, behind an interface so
// UpdateApplier's decisions are tested without a real service.
public interface IServiceControl
{
    // Full path of the exe the service starts (without quotes).
    string? GetBinaryPath();

    // Through the SCM (ChangeServiceConfig), then flushed to disk, so the
    // switch holds through a power cut. A plain registry write isn't seen
    // by the SCM until a reboot.
    void SetBinaryPath(string exePath);

    ServiceSnapshot Query();

    // Both return at once; "already stopped/running" is not an error.
    void RequestStop();
    void RequestStart();

    void KillProcess(int processId);
}

public sealed class WindowsServiceControl : IServiceControl
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceNoChange = 0xFFFFFFFF;
    private const uint ServiceControlStop = 0x00000001;
    private const int ScStatusProcessInfo = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceNotActive = 1062;
    private const int ErrorServiceCannotAcceptControl = 1061;

    private readonly string _serviceName;

    public WindowsServiceControl(string serviceName = UpdatePaths.ServiceName)
    {
        _serviceName = serviceName;
    }

    public string? GetBinaryPath()
    {
        using var service = Open(ServiceQueryConfig);
        QueryServiceConfig(service.Handle, IntPtr.Zero, 0, out var needed);
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!QueryServiceConfig(service.Handle, buffer, needed, out _))
            {
                throw new Win32Exception();
            }
            var config = Marshal.PtrToStructure<QUERY_SERVICE_CONFIG>(buffer);
            return ParseBinaryPath(Marshal.PtrToStringUni(config.lpBinaryPathName));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void SetBinaryPath(string exePath)
    {
        using (var service = Open(ServiceChangeConfig))
        {
            if (!ChangeServiceConfig(service.Handle, ServiceNoChange, ServiceNoChange, ServiceNoChange,
                    $"\"{exePath}\"", null, IntPtr.Zero, null, null, null, null))
            {
                throw new Win32Exception();
            }
        }

        // The SCM wrote ImagePath into the registry, which reaches the disk
        // lazily; the update record is flushed right after, so this should
        // be on disk first. RegistryKey.Flush would do nothing here (it only
        // flushes a key its own instance wrote to). Never thrown: the switch
        // itself has happened, and callers must know that.
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{_serviceName}", writable: true);
        var flushed = key is null ? -1 : RegFlushKey(key.Handle);
        if (flushed != 0)
        {
            AgentLog.Warn($"Couldn't flush the service's new binary path to disk (error {flushed}); Windows will write it shortly.");
        }
    }

    public ServiceSnapshot Query()
    {
        using var service = Open(ServiceQueryStatus);
        var size = Marshal.SizeOf<SERVICE_STATUS_PROCESS>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryServiceStatusEx(service.Handle, ScStatusProcessInfo, buffer, (uint)size, out _))
            {
                throw new Win32Exception();
            }
            var status = Marshal.PtrToStructure<SERVICE_STATUS_PROCESS>(buffer);
            var state = status.dwCurrentState switch
            {
                1 => ServiceRunState.Stopped,
                2 => ServiceRunState.StartPending,
                3 => ServiceRunState.StopPending,
                4 => ServiceRunState.Running,
                _ => ServiceRunState.Other,
            };
            return new ServiceSnapshot(state, (int)status.dwProcessId);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void RequestStop()
    {
        using var service = Open(ServiceStop);
        if (!ControlService(service.Handle, ServiceControlStop, out _))
        {
            var error = Marshal.GetLastWin32Error();
            if (error is not (ErrorServiceNotActive or ErrorServiceCannotAcceptControl))
            {
                throw new Win32Exception(error);
            }
        }
    }

    public void RequestStart()
    {
        using var service = Open(ServiceStart);
        if (!StartService(service.Handle, 0, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorServiceAlreadyRunning)
            {
                throw new Win32Exception(error);
            }
        }
    }

    public void KillProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }

    // "\"C:\x\Midview.Agent.exe\" args" or an unquoted path.
    public static string? ParseBinaryPath(string? commandLine)
    {
        var text = commandLine?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }
        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? text[..(exe + 4)] : text;
    }

    private ServiceHandle Open(uint access)
    {
        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception();
        }

        var service = OpenService(manager, _serviceName, access);
        if (service == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            CloseServiceHandle(manager);
            throw new Win32Exception(error);
        }
        return new ServiceHandle(manager, service);
    }

    private sealed class ServiceHandle : IDisposable
    {
        private readonly IntPtr _manager;

        public ServiceHandle(IntPtr manager, IntPtr handle)
        {
            _manager = manager;
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public void Dispose()
        {
            CloseServiceHandle(Handle);
            CloseServiceHandle(_manager);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QUERY_SERVICE_CONFIG
    {
        public uint dwServiceType;
        public uint dwStartType;
        public uint dwErrorControl;
        public IntPtr lpBinaryPathName;
        public IntPtr lpLoadOrderGroup;
        public uint dwTagId;
        public IntPtr lpDependencies;
        public IntPtr lpServiceStartName;
        public IntPtr lpDisplayName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenSCManagerW")]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenServiceW")]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryServiceConfigW")]
    private static extern bool QueryServiceConfig(IntPtr service, IntPtr config, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ChangeServiceConfigW")]
    private static extern bool ChangeServiceConfig(
        IntPtr service, uint serviceType, uint startType, uint errorControl, string? binaryPathName,
        string? loadOrderGroup, IntPtr tagId, string? dependencies, string? serviceStartName, string? password,
        string? displayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr service, uint control, out SERVICE_STATUS status);

    [DllImport("advapi32.dll")]
    private static extern int RegFlushKey(Microsoft.Win32.SafeHandles.SafeRegistryHandle key);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "StartServiceW")]
    private static extern bool StartService(IntPtr service, int argumentCount, IntPtr arguments);
}

// The boot task that undoes an interrupted update: run by the build being
// replaced, as SYSTEM, once at the next start of Windows.
public interface IRecoveryTask
{
    void Register(string oldAgentExe);

    void Delete();
}

public sealed class SchtasksRecoveryTask : IRecoveryTask
{
    private static readonly XNamespace Task = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly string _taskName;

    public SchtasksRecoveryTask(string taskName = UpdatePaths.RecoveryTaskName)
    {
        _taskName = taskName;
    }

    public void Register(string oldAgentExe)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"midview-recover-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildXml(oldAgentExe), System.Text.Encoding.Unicode);
            var (code, output) = RunSchtasks("/Create", "/TN", _taskName, "/XML", xmlPath, "/F");
            if (code != 0)
            {
                throw new IOException($"schtasks não criou a tarefa de recuperação: {output.Trim()}");
            }
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    public void Delete()
    {
        if (RunSchtasks("/Query", "/TN", _taskName).ExitCode == 0)
        {
            RunSchtasks("/Delete", "/TN", _taskName, "/F");
        }
    }

    public static string BuildXml(string oldAgentExe)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Task + "Task", new XAttribute("version", "1.2"),
                new XElement(Task + "RegistrationInfo",
                    new XElement(Task + "Description",
                        "Midview: desfaz uma atualização pela rede que foi interrompida, voltando para a versão anterior.")),
                new XElement(Task + "Triggers",
                    new XElement(Task + "BootTrigger",
                        new XElement(Task + "Enabled", "true"),
                        new XElement(Task + "Delay", "PT10S"))),
                new XElement(Task + "Principals",
                    new XElement(Task + "Principal", new XAttribute("id", "Author"),
                        new XElement(Task + "UserId", "S-1-5-18"),
                        new XElement(Task + "RunLevel", "HighestAvailable"))),
                new XElement(Task + "Settings",
                    new XElement(Task + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Task + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Task + "StopIfGoingOnBatteries", "false"),
                    new XElement(Task + "ExecutionTimeLimit", "PT30M"),
                    new XElement(Task + "Enabled", "true")),
                new XElement(Task + "Actions", new XAttribute("Context", "Author"),
                    new XElement(Task + "Exec",
                        new XElement(Task + "Command", oldAgentExe),
                        new XElement(Task + "Arguments", UpdateCommands.RecoverArgument)))));

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        document.Save(writer);
        return writer.ToString();
    }

    private static (int ExitCode, string Output) RunSchtasks(params string[] args)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
    }
}

public static class SessionProcesses
{
    internal const string SessionName = "Midview.Session";
    internal const string HelperName = "Midview.Desk";

    // What KillAll ends, by name only: the applier is a Midview.Agent too.
    internal static readonly IReadOnlyList<string> Names = new[] { SessionName, HelperName };

    // Every Midview.Session and every Midview.Desk, in every logon session: a
    // leftover one (from either build) keeps its port -- 5901 the Session, 5902
    // the helper, which also locks its own exe -- and never exits on its own.
    public static void KillAll()
    {
        foreach (var name in Names)
        {
            KillByName(name);
        }
    }

    // Every process with this name, in every logon session.
    internal static void KillByName(string name)
    {
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(10_000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Already exiting.
                }
            }
        }
    }
}
