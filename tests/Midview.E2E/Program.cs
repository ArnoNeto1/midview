using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;

// Plays the teacher's MidviewMaster against the Agent installed on this
// machine, for the real-Windows check of the network update
// (installer/tests/Testar-Atualizacao.ps1, run by
// .github/workflows/atualizacao.yml):
//
//   authorize <package> <teacher.pfx>
//       creates a teacher and authorizes it in the package, before installing
//   status <package> <teacher.pfx> [<expected package>]
//       waits for the Agent to report its build (the expected package's)
//   push <package> <new package> <teacher.pfx> <committed|rolledBack>
//       updates over the network and follows the attempt to the end
//   block <package> <teacher.pfx> <blocklist|allowonly|lift> [programs...]
//       applies a program block to the machine and reads it back
//   block-status <package> <teacher.pfx>
//       prints the block the machine follows
//   facts
//       prints this process's session, the console session and whether it
//       is elevated (what program blocking looks at)
//   launch <exe> [arguments...]
//       starts a program with a limited (non-elevated) token of this user,
//       the way a student's program runs; prints pid=<id>
//
// <package> is the one the machine was installed from: its maquinas\ folder
// has the machine's file. Exit code 0 means the expected happened.
return args switch
{
    ["authorize", var package, var pfx] => Authorize(package, pfx),
    ["status", var package, var pfx] => Status(package, pfx, null),
    ["status", var package, var pfx, var expected] => Status(package, pfx, expected),
    ["push", var package, var update, var pfx, var expected] => Push(package, update, pfx, expected),
    ["block", var package, var pfx, var mode, .. var programs] => Block(package, pfx, mode, programs),
    ["block-status", var package, var pfx] => BlockStatus(package, pfx),
    ["facts"] => Facts(),
    ["launch", var exe, .. var rest] => Launch(exe, rest),
    _ => Usage(),
};

static int Block(string package, string pfx, string mode, string[] programs)
{
    using var teacher = LoadTeacher(pfx);
    var machine = InstalledMachine(package);
    var blockMode = mode switch
    {
        "blocklist" => AppBlockMode.BlockList,
        "allowonly" => AppBlockMode.AllowOnly,
        "lift" => AppBlockMode.None,
        _ => throw new ArgumentException($"unknown mode {mode}"),
    };

    // Newer than whatever the machine has (a second can pass between two
    // calls of this tool, or not).
    var current = AppBlockClient.GetStatus(machine, teacher).Policy;
    var savedAt = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), (current?.SavedAt ?? 0) + 1);
    var policy = new AppBlockPolicy(savedAt, blockMode, blockMode == AppBlockMode.None ? Array.Empty<string>() : programs);

    var (succeeded, detail) = AppBlockClient.Apply(machine, teacher, policy);
    Say($"block {mode} [{string.Join(", ", programs)}]: {(succeeded ? "ok" : "refused")} - {detail}");
    return succeeded ? 0 : 1;
}

static int BlockStatus(string package, string pfx)
{
    using var teacher = LoadTeacher(pfx);
    var status = AppBlockClient.GetStatus(InstalledMachine(package), teacher);
    if (status.OldAgent || status.Policy is null)
    {
        Say("the Agent doesn't know program blocking");
        return 1;
    }
    Say($"status: mode={status.Policy.Mode} savedAt={status.Policy.SavedAt} programs=[{string.Join(", ", status.Policy.Programs)}]");
    return 0;
}

static int Facts()
{
    var session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
    Say($"session={session}");
    Say($"console={NativeMethods.WTSGetActiveConsoleSessionId()}");
    Say($"elevated={NativeMethods.IsElevated()}");
    return 0;
}

static int Launch(string exe, string[] arguments)
{
    var pid = NativeMethods.StartLimited(exe, arguments, out var exitedWith);
    if (pid is null)
    {
        Say("launch failed");
        return 1;
    }
    if (exitedWith is { } code)
    {
        // Gone by itself within moments of starting: nothing a block did.
        Say($"pid={pid} exited by itself with code {code} (0x{code:X})");
        return 3;
    }
    Say($"pid={pid}");
    return 0;
}

static int Authorize(string package, string pfx)
{
    using var teacher = SelfSignedCertificate.Create("midview-e2e-teacher");
    File.WriteAllBytes(pfx, teacher.Export(X509ContentType.Pfx));
    InstallPackage.AuthorizeMaster(package, teacher.Thumbprint);
    Say($"teacher {teacher.Thumbprint} authorized in {package}");
    return 0;
}

static int Status(string package, string pfx, string? expectedPackage)
{
    using var teacher = LoadTeacher(pfx);
    var machine = InstalledMachine(package);
    var expected = expectedPackage is null ? null : Convert.ToHexString(UpdatePackage.Identify(expectedPackage).Fingerprint);
    var report = WaitForBuild(machine, teacher, TimeSpan.FromMinutes(3),
        report => expected is null || string.Equals(report.Fingerprint, expected, StringComparison.OrdinalIgnoreCase));
    if (report is null)
    {
        Say(expected is null ? "the Agent never reported its build" : $"the Agent never reported build {expected}");
        return 1;
    }
    return 0;
}

static int Push(string package, string updatePackage, string pfx, string expectedState)
{
    using var teacher = LoadTeacher(pfx);
    var machine = InstalledMachine(package);
    using var update = UpdatePackage.Create(updatePackage, Path.GetTempPath());
    var fingerprint = Convert.ToHexString(update.Fingerprint);
    var attemptId = RandomNumberGenerator.GetBytes(NetworkUpdate.AttemptIdLength);
    var attemptHex = Convert.ToHexString(attemptId);
    Say($"pushing {update.Manifest.Build} ({fingerprint}, {update.ZipSize / (1024 * 1024)} MB zipped) as attempt {attemptHex}");

    var push = UpdateClient.Push(machine, teacher, update, attemptId);
    Say($"push: {push.Outcome} {push.Detail}");
    if (push.Outcome is not (PushOutcome.Received or PushOutcome.SentNoAnswer))
    {
        return 1;
    }

    // As UpdateWindow follows it: by the attempt's id (which is also what
    // lets the new build confirm itself).
    var deadline = DateTime.UtcNow + NetworkUpdate.MasterDeadline;
    string? last = null;
    UpdateAttemptReport? attempt = null;
    while (DateTime.UtcNow < deadline)
    {
        Thread.Sleep(NetworkUpdate.PollInterval);
        try
        {
            var status = UpdateClient.GetStatus(machine, teacher, attemptId);
            if (status.Report?.Attempt is { } current && current.Id == attemptHex)
            {
                attempt = current;
                var line = $"{current.State}: {current.Detail}";
                if (line != last)
                {
                    Say(line);
                    last = line;
                }
                if (UpdateStates.IsFinal(current.State) || current.State == UpdateStates.RollbackPending)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Say($"(not answering: {ex.Message})");
        }
    }

    if (attempt is null || attempt.State != expectedState)
    {
        Say($"expected the attempt to end {expectedState}, it ended {attempt?.State ?? "without an answer"}");
        return 1;
    }

    // And the machine runs what that outcome means.
    var running = WaitForBuild(machine, teacher, TimeSpan.FromMinutes(3), report =>
        string.Equals(report.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase) == (expectedState == UpdateStates.Committed));
    if (running is null)
    {
        Say(expectedState == UpdateStates.Committed ? "the new build isn't the one running" : "the new build is still running");
        return 1;
    }
    return 0;
}

// Polls until the Agent reports a computed build that satisfies accept, or
// the timeout passes (null).
static UpdateStatusReport? WaitForBuild(KnownMachine machine, X509Certificate2 teacher, TimeSpan timeout, Func<UpdateStatusReport, bool> accept)
{
    var deadline = DateTime.UtcNow + timeout;
    string? last = null;
    while (true)
    {
        try
        {
            var status = UpdateClient.GetStatus(machine, teacher);
            if (status.OldAgent)
            {
                Say("the Agent doesn't know network updates");
                return null;
            }
            var report = status.Report!;
            var line = $"installed: {(report.Ready ? $"{report.Build} ({report.Fingerprint})" : "still computing")}";
            if (line != last)
            {
                Say(line);
                last = line;
            }
            if (report.Ready && accept(report))
            {
                return report;
            }
        }
        catch (Exception ex)
        {
            Say($"(not answering: {ex.Message})");
        }

        if (DateTime.UtcNow >= deadline)
        {
            return null;
        }
        Thread.Sleep(TimeSpan.FromSeconds(3));
    }
}

static X509Certificate2 LoadTeacher(string pfx) =>
    new(File.ReadAllBytes(pfx), (string?)null, X509KeyStorageFlags.Exportable);

// Over loopback: the machine file's address is this machine anyway.
static KnownMachine InstalledMachine(string package)
{
    var (machines, problems) = InstallPackage.LoadInstalledMachines(package, useComputerName: false);
    if (machines.Count != 1)
    {
        throw new InvalidOperationException(
            $"expected one machine file in {package}, found {machines.Count} ({string.Join("; ", problems)})");
    }
    return machines[0] with { HostAddress = "127.0.0.1" };
}

static void Say(string line) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");

static int Usage()
{
    Console.WriteLine("usage: authorize <package> <pfx> | status <package> <pfx> [<expected package>] | push <package> <new package> <pfx> <committed|rolledBack>");
    return 2;
}

// Win32 for the two things the test needs that .NET doesn't give: whether a
// token is elevated, and a process started with a limited token of the same
// user (an administrator's filtered token), which is how a student's
// programs run: the Agent never touches elevated ones.
internal static class NativeMethods
{
    private const uint TokenQuery = 0x0008;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenAssignPrimary = 0x0001;
    private const int TokenElevationClass = 20;
    private const uint LuaToken = 0x4;
    private const uint CreateNoWindow = 0x08000000;

    public static bool IsElevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
        {
            return false;
        }
        try
        {
            var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(int));
            try
            {
                return GetTokenInformation(token, TokenElevationClass, buffer, sizeof(int), out _)
                       && System.Runtime.InteropServices.Marshal.ReadInt32(buffer) != 0;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    // exitedWith: set when the program ended on its own within 3 seconds.
    public static int? StartLimited(string exe, string[] arguments, out int? exitedWith)
    {
        exitedWith = null;
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery | TokenDuplicate | TokenAssignPrimary, out var token))
        {
            return null;
        }
        try
        {
            if (!CreateRestrictedToken(token, LuaToken, 0, IntPtr.Zero, 0, IntPtr.Zero, 0, IntPtr.Zero, out var limited))
            {
                return null;
            }
            try
            {
                var startup = new StartupInfo { cb = System.Runtime.InteropServices.Marshal.SizeOf<StartupInfo>() };
                var commandLine = new System.Text.StringBuilder($"\"{exe}\"");
                foreach (var argument in arguments)
                {
                    commandLine.Append(' ').Append(argument);
                }
                if (!CreateProcessAsUser(limited, null, commandLine, IntPtr.Zero, IntPtr.Zero, false, CreateNoWindow,
                        IntPtr.Zero, null, ref startup, out var info))
                {
                    Console.WriteLine($"CreateProcessAsUser failed: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                    return null;
                }
                try
                {
                    if (WaitForSingleObject(info.hProcess, 3000) == 0 && GetExitCodeProcess(info.hProcess, out var code))
                    {
                        exitedWith = unchecked((int)code);
                    }
                }
                finally
                {
                    CloseHandle(info.hProcess);
                    CloseHandle(info.hThread);
                }
                return info.dwProcessId;
            }
            finally
            {
                CloseHandle(limited);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    public static extern uint WTSGetActiveConsoleSessionId();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, int length, out int returnLength);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CreateRestrictedToken(
        IntPtr existing, uint flags, uint disableSidCount, IntPtr sidsToDisable, uint deletePrivilegeCount,
        IntPtr privilegesToDelete, uint restrictedSidCount, IntPtr sidsToRestrict, out IntPtr newToken);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "CreateProcessAsUserW")]
    private static extern bool CreateProcessAsUser(
        IntPtr token, string? applicationName, System.Text.StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);
}
