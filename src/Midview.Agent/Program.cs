using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Midview.Agent;
using Midview.Protocol;

// Deployed today as loose files side by side on each lab machine (no
// separate per-component folders), so Session.exe lives right next to
// this exe rather than in a sibling directory.
var sessionExecutablePath = Path.Combine(AppContext.BaseDirectory, "Midview.Session.exe");

// Before either hosted service starts: SessionSupervisor launches Session
// immediately, and Session -- running as the logged-in student -- can read
// this directory but never create anything in it, so its persisted identity
// has to exist first. Its thumbprint is logged because the auto-launched
// Session has no console to print it to, and agent.log is readable by any user.
try
{
    AgentDataDirectory.EnsureSecure();
    using var sessionCertificate = SessionIdentity.LoadOrCreate(SessionIdentity.DefaultCertificatePath);
    AgentLog.Warn($"Session certificate thumbprint: {sessionCertificate.Thumbprint}");
}
catch (Exception ex)
{
    AgentLog.Warn($"Failed to prepare the data directory or the Session identity: {ex.Message}");
}

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "MidviewAgent")
    .ConfigureServices(services =>
    {
        services.AddSingleton<ISessionProcessLauncher>(
            new UserSessionProcessLauncher(sessionExecutablePath));
        services.AddSingleton(provider => new SessionSupervisor(
            provider.GetRequiredService<ISessionProcessLauncher>(), TimeSpan.FromSeconds(5)));
        services.AddHostedService<MidviewAgentWorker>();
        services.AddHostedService<AgentServerWorker>();
    })
    .Build();

host.Run();
