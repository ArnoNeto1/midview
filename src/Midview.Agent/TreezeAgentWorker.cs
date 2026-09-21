using Microsoft.Extensions.Hosting;

namespace Midview.Agent;

public sealed class MidviewAgentWorker : BackgroundService
{
    private readonly SessionSupervisor _supervisor;

    public MidviewAgentWorker(SessionSupervisor supervisor)
    {
        _supervisor = supervisor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _supervisor.EnsureRunning();
            await Task.Delay(_supervisor.PollInterval, stoppingToken);
        }
    }
}
