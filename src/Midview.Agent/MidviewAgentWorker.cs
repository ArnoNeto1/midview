using Microsoft.Extensions.Hosting;

namespace Midview.Agent;

public sealed class MidviewAgentWorker : BackgroundService
{
    private readonly SessionSupervisor _supervisor;
    private readonly StudentFolderGuard _folderGuard;
    private readonly ClassLessonHandler _classLesson;

    public MidviewAgentWorker(SessionSupervisor supervisor, StudentFolderGuard folderGuard, ClassLessonHandler classLesson)
    {
        _supervisor = supervisor;
        _folderGuard = folderGuard;
        _classLesson = classLesson;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _supervisor.EnsureRunning();
            _folderGuard.Check();
            // The class folder and the download folder follow the lesson for
            // whoever is logged on; a lesson from an earlier day ends here.
            _classLesson.Reconcile();
            await Task.Delay(_supervisor.PollInterval, stoppingToken);
        }
    }
}
