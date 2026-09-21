using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TreezeSuite.Agent;

var sessionExecutablePath = Path.Combine(AppContext.BaseDirectory, "..", "TreezeSuite.Session", "TreezeSuite.Session.exe");

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "TreezeAgent")
    .ConfigureServices(services =>
    {
        services.AddSingleton<ISessionProcessLauncher>(
            new UserSessionProcessLauncher(sessionExecutablePath));
        services.AddSingleton(provider => new SessionSupervisor(
            provider.GetRequiredService<ISessionProcessLauncher>(), TimeSpan.FromSeconds(5)));
        services.AddHostedService<TreezeAgentWorker>();
    })
    .Build();

host.Run();
