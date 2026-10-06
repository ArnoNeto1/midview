using Microsoft.Extensions.Hosting;

namespace Midview.Agent;

public sealed class MidviewAgentWorker : BackgroundService
{
    private readonly SessionSupervisor _supervisor;
    private readonly StudentFolderGuard _folderGuard;

    public MidviewAgentWorker(SessionSupervisor supervisor, StudentFolderGuard folderGuard)
    {
        _supervisor = supervisor;
        _folderGuard = folderGuard;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _supervisor.EnsureRunning();
            _folderGuard.Check();
            await Task.Delay(_supervisor.PollInterval, stoppingToken);
        }
    }
}
