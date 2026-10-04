using Microsoft.Extensions.Hosting;

namespace Midview.Agent;

// Runs the enforcer every second, starting from the policy saved on disk.
// A failure in one tick is logged and never stops the Agent.
public sealed class AppBlockWorker : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private readonly AppBlockEnforcer _enforcer;

    public AppBlockWorker(AppBlockEnforcer enforcer, AppBlockCommandHandler handler)
    {
        _enforcer = enforcer;
        _enforcer.Apply(handler.Current);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failing = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _enforcer.EnforceOnce();
                failing = false;
            }
            catch (Exception ex)
            {
                if (!failing)
                {
                    AgentLog.Warn($"Program blocking failed a check: {ex.Message}");
                    failing = true;
                }
            }

            try
            {
                await Task.Delay(Tick, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
