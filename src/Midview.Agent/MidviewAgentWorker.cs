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
        EndHelper();

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

    // The helper outlives the service (a SYSTEM process in the console session), so
    // it is ended here, when the service stops gracefully (SCM stop, restart): a
    // SYSTEM process is always allowed to end it, which an administrator's
    // Stop-Process in Stop-Midview might not be. After the loop has stopped, so the
    // supervisor can't launch it again in between. A crash skips this; the clear at
    // start is the backstop. Only the helper: the Session stays, as it always has.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            EndHelper();
        }
    }

    // Isolated like the steps in Program.cs: nothing here may keep the Agent from
    // starting or from stopping, and so from listening for the teacher's commands.
    private void EndHelper()
    {
        try
        {
            _clearStrayHelper();
        }
        catch (Exception ex)
        {
            _log($"Couldn't end the Desk helper: {ex.Message}");
        }
    }
}
