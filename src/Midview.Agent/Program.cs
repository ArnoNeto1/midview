using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Midview.Agent;

// Deployed today as loose files side by side on each lab machine (no
// separate per-component folders), so Session.exe lives right next to
// this exe rather than in a sibling directory.
var sessionExecutablePath = Path.Combine(AppContext.BaseDirectory, "Midview.Session.exe");

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
