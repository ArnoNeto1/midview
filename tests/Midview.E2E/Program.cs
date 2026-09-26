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
//
// <package> is the one the machine was installed from: its maquinas\ folder
// has the machine's file. Exit code 0 means the expected happened.
return args switch
{
    ["authorize", var package, var pfx] => Authorize(package, pfx),
    ["status", var package, var pfx] => Status(package, pfx, null),
    ["status", var package, var pfx, var expected] => Status(package, pfx, expected),
    ["push", var package, var update, var pfx, var expected] => Push(package, update, pfx, expected),
    _ => Usage(),
};

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
