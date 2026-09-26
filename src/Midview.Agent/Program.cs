using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Midview.Agent;
using Midview.Protocol;

// Run by the installer scripts (installer/), not by the service.
if (args is [InstallCommands.PrepareInstallArgument])
{
    return InstallCommands.RunPrepareInstall();
}
if (args is [InstallCommands.UninstallCleanupArgument])
{
    return InstallCommands.RunUninstallCleanup();
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
