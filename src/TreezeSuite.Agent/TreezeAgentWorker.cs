using Microsoft.Extensions.Hosting;

namespace TreezeSuite.Agent;

public sealed class TreezeAgentWorker : BackgroundService
{
    private readonly SessionSupervisor _supervisor;

    public TreezeAgentWorker(SessionSupervisor supervisor)
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
