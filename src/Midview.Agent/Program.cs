using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Midview.Agent;
using Midview.Protocol;

// The SCM starts the service with no arguments. The installer scripts
// (installer/) and a network update run the Agent with exactly one known
// flag; anything else is refused rather than falling through to a second,
// console-mode Agent running as whoever started it.
if (args.Length > 0)
{
    return args switch
    {
        [InstallCommands.PrepareInstallArgument] => InstallCommands.RunPrepareInstall(),
        [InstallCommands.UninstallCleanupArgument] => InstallCommands.RunUninstallCleanup(),
        [UpdateCommands.ApplyArgument] => UpdateCommands.RunApply(),
        [UpdateCommands.RecoverArgument] => UpdateCommands.RunRecover(),
        _ => RefuseUnknownArguments(args),
    };
}

// Deployed today as loose files side by side on each lab machine (no
// separate per-component folders), so Session.exe lives right next to
// this exe rather than in a sibling directory.
var sessionExecutablePath = Path.Combine(AppContext.BaseDirectory, "Midview.Session.exe");
// The Desk helper sits next to this exe too.
var deskExecutablePath = Path.Combine(AppContext.BaseDirectory, "Midview.Desk.exe");

// Before the host starts, so the directory's permissions and Session's
// certificate are in place before SessionSupervisor launches the first
// Session.
PrepareDataDirectory();

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "MidviewAgent")
    .ConfigureServices(services =>
    {
        services.AddSingleton<ISessionProcessLauncher>(
            new UserSessionProcessLauncher(sessionExecutablePath));
        services.AddSingleton(provider => new SessionSupervisor(
            provider.GetRequiredService<ISessionProcessLauncher>(), TimeSpan.FromSeconds(5)));
        // On the same poll: the Desk helper, which can follow the screen onto
        // the UAC prompt where Session (the student's own process) can't. A type
        // of its own, so it isn't mistaken for the SessionSupervisor above.
        services.AddSingleton(new HelperSupervisor(new SessionSupervisor(
            new ConsoleSystemProcessLauncher(deskExecutablePath), TimeSpan.FromSeconds(5))));
        // On the same poll: the student's two Desktop folders come back,
        // protected, if someone deletes them.
        services.AddSingleton(new StudentFolderGuard(
            UserSessionProcessLauncher.LoggedOnStudent, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
        services.AddSingleton(new InputLockCoordinator(new WindowsInputDeviceController()));
        // The countdown on the student's screen while a shutdown is on its
        // way, shared by the teacher's and the schedule's.
        services.AddSingleton<ICountdownDisplay>(new SessionCountdownDisplay(new UserSessionProcessLauncher(sessionExecutablePath)));
        services.AddSingleton(provider => new ScheduledShutdown(
            ScheduledShutdown.DefaultStatePath, PowerCommandHandler.RunShutdownExe, provider.GetRequiredService<ICountdownDisplay>()));
        services.AddSingleton(provider => new PowerCommandHandler(
            PowerCommandHandler.RunShutdownExe, countdown: provider.GetRequiredService<ICountdownDisplay>(),
            runUpdatingShutdown: WindowsUpdatingShutdown.Initiate));
        services.AddSingleton(provider =>
        {
            var runningExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, UpdatePaths.AgentExeName);
            var schedule = provider.GetRequiredService<ScheduledShutdown>();
            var power = provider.GetRequiredService<PowerCommandHandler>();
            return new UpdateReceiver(
                UpdatePaths.Default, UpdatePaths.UsbInstallFolder, runningExe, new InstalledBuild(AppContext.BaseDirectory),
                () => schedule.IsDueWithin(DateTime.Now, TimeSpan.FromMinutes(20)) || power.IsShutdownPending(),
                UpdateReceiver.FreeBytesOn, new ProcessApplierLauncher(runningExe), SystemUpdateClock.Instance);
        });
        services.AddSingleton<IProcessInspector>(new WindowsProcessInspector());
        services.AddSingleton(provider => new AppBlockEnforcer(provider.GetRequiredService<IProcessInspector>()));
        services.AddSingleton(provider =>
        {
            var enforcer = provider.GetRequiredService<AppBlockEnforcer>();
            return new AppBlockCommandHandler(AppBlockCommandHandler.DefaultStatePath, enforcer.Apply, UacCheck.Warning);
        });
        // The lab-configuration panel: writes Chrome and Windows policies
        // (the enforcer ends Edge for it). One writer open on HKLM for the
        // Agent's life.
        services.AddSingleton(provider => new LabConfigCommandHandler(
            LabConfigCommandHandler.DefaultStatePath, LabPolicyWriter.OpenMachine(),
            provider.GetRequiredService<AppBlockEnforcer>()));
        // The class folder's lesson: the protected "Pasta da turma" folder and
        // the download folder in the logged-on student's own registry hive
        // (the enforcer closes Chrome when it changes). It keeps which teacher
        // PC holds the class folders when a lab has two: only that one starts
        // a lesson.
        services.AddSingleton(provider => new ClassLessonHandler(
            ClassLessonFile.DefaultPath, UserSessionProcessLauncher.LoggedOnStudent, DownloadFolderPolicy.OpenUserSoftware,
            provider.GetRequiredService<AppBlockEnforcer>(), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            () => DateTime.Now, home: new ClassHomeState(ClassHomeState.DefaultStatePath)));
        // Lists and silently uninstalls installed programs, for the console's
        // "Desinstalar programa".
        services.AddSingleton(_ => ProgramUninstaller.OpenMachine());
        // What the teacher asked to remove, kept for the students who weren't
        // logged on then (PendingUninstallWorker removes their copies at logon).
        services.AddSingleton(_ => new PendingUninstalls(PendingUninstalls.DefaultPath));
        // Installs what the teacher sends, silently and as SYSTEM, for the
        // console's "Instalar programa".
        services.AddSingleton(_ => new ProgramInstaller(
            ProgramInstaller.DefaultFolder, UpdateReceiver.FreeBytesOn, ProgramInstaller.RunProcess));
        // trusted-masters.json, read again whenever pairing (or anything
        // else) changes it.
        services.AddSingleton(TrustStore.FromFile(AgentServerWorker.TrustedMastersPath));
        services.AddSingleton(provider => new PairingService(
            provider.GetRequiredService<TrustStore>(), AgentServerWorker.TrustedMastersPath,
            new SessionPairingCodeDisplay(new UserSessionProcessLauncher(sessionExecutablePath)),
            () => DateTime.Now, Environment.MachineName,
            () => SessionIdentity.EnsureExists(SessionIdentity.DefaultCertificatePath)));
        services.AddHostedService<MidviewAgentWorker>();
        services.AddHostedService<AgentServerWorker>();
        services.AddHostedService<DiscoveryWorker>();
        services.AddHostedService<InputLockWorker>();
        services.AddHostedService<ScheduledShutdownWorker>();
        services.AddHostedService<AppBlockWorker>();
        services.AddHostedService<PendingUninstallWorker>();
    })
    .Build();

host.Run();
return 0;

// Each step isolated from the others: none of them may keep the Agent from
// starting (and so from listening for the teacher's commands).
static void PrepareDataDirectory()
{
    TryStep("secure the data directory", () => AgentDataDirectory.EnsureSecure());

    // The directory lets Users read everything in it (Session needs that),
    // so the Agent's own private key gets a stricter ACL of its own.
    TryStep("create or protect the Agent's certificate", () =>
    {
        using var certificate = AgentIdentity.LoadOrCreate(AgentIdentity.DefaultCertificatePath);
        AgentDataDirectory.RestrictToAdministrators(AgentIdentity.DefaultCertificatePath);
        AgentLog.Warn($"Agent certificate thumbprint: {certificate.Thumbprint}");
    });

    TryStep("create Session's certificate", () =>
    {
        var thumbprint = SessionIdentity.EnsureExists(SessionIdentity.DefaultCertificatePath);
        AgentLog.Warn($"Session certificate thumbprint: {thumbprint}");
    });

    // Network updates keep their lock and record where students can't
    // open them (see UpdatePaths.EnsureStateDirectory).
    TryStep("protect the network update folder", () => UpdatePaths.Default.EnsureStateDirectory());
}

static int RefuseUnknownArguments(string[] args)
{
    var message = $"Unknown arguments, not starting: {string.Join(' ', args)}";
    AgentLog.Warn(message);
    Console.Error.WriteLine(message);
    return 2;
}

static void TryStep(string description, Action step)
{
    try
    {
        step();
    }
    catch (Exception ex)
    {
        AgentLog.Warn($"Failed to {description}: {ex.Message}");
    }
}
