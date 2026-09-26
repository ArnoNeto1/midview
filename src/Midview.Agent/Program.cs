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
        services.AddSingleton(new InputLockCoordinator(new WindowsInputDeviceController()));
        services.AddSingleton(_ => new ScheduledShutdown(ScheduledShutdown.DefaultStatePath, PowerCommandHandler.RunShutdownExe));
        services.AddSingleton(_ => new PowerCommandHandler(PowerCommandHandler.RunShutdownExe));
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
        services.AddHostedService<MidviewAgentWorker>();
        services.AddHostedService<AgentServerWorker>();
        services.AddHostedService<InputLockWorker>();
        services.AddHostedService<ScheduledShutdownWorker>();
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
