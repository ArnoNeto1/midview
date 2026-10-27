using Microsoft.Extensions.Hosting;

namespace Midview.Agent;

public sealed class MidviewAgentWorker : BackgroundService
{
    private readonly SessionSupervisor _supervisor;
    private readonly HelperSupervisor _helperSupervisor;
    private readonly StudentFolderGuard _folderGuard;
    private readonly ClassLessonHandler _classLesson;
    private readonly Action _clearStrayHelper;
    private readonly Action<string> _log;

    public MidviewAgentWorker(
        SessionSupervisor supervisor, HelperSupervisor helperSupervisor, StudentFolderGuard folderGuard,
        ClassLessonHandler classLesson)
        : this(supervisor, helperSupervisor, folderGuard, classLesson,
            () => SessionProcesses.KillByName(SessionProcesses.HelperName), AgentLog.Warn)
    {
    }

    // Ending a leftover helper and the log are injectable so a test can run the
    // loop without ending real processes or writing the real agent.log.
    internal MidviewAgentWorker(
        SessionSupervisor supervisor, HelperSupervisor helperSupervisor, StudentFolderGuard folderGuard,
        ClassLessonHandler classLesson, Action clearStrayHelper, Action<string> log)
    {
        _supervisor = supervisor;
        _helperSupervisor = helperSupervisor;
        _folderGuard = folderGuard;
        _classLesson = classLesson;
        _clearStrayHelper = clearStrayHelper;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A helper left by an earlier Agent (a restart, a crash, an update) still
        // holds its port and its exe, and the supervisor can't adopt it: it doesn't
        // know its pid, so a second helper would only fail to bind, every poll. It
        // is ended once, here, and the first pass below starts a fresh one. Only
        // the helper: the Session that is running stays.
        ClearStrayHelper();

        while (!stoppingToken.IsCancellationRequested)
        {
            _supervisor.EnsureRunning();
            _helperSupervisor.EnsureRunning();
            _folderGuard.Check();
            // The class folder and the download folder follow the lesson for
            // whoever is logged on; a lesson from an earlier day ends here.
            _classLesson.Reconcile();
            await Task.Delay(_supervisor.PollInterval, stoppingToken);
        }
    }

    // Isolated like the steps in Program.cs: nothing here may keep the Agent from
    // starting, and so from listening for the teacher's commands.
    private void ClearStrayHelper()
    {
        try
        {
            _clearStrayHelper();
        }
        catch (Exception ex)
        {
            _log($"Couldn't end a leftover Desk helper: {ex.Message}");
        }
    }
}
