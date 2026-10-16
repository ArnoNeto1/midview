using Microsoft.Extensions.Hosting;

namespace Midview.Agent;

// Removes the programs a teacher asked to remove (PendingUninstalls) from
// each student who logs on afterwards: their "just for me" copies, which the
// teacher's request couldn't reach while they weren't logged on. Looks at the
// console every Tick; a failure in one look is logged and never stops the
// Agent.
public sealed class PendingUninstallWorker : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    private readonly Func<string?> _loggedOnStudent;
    private readonly PendingUninstalls _pending;
    private readonly Func<IReadOnlyList<string>, IReadOnlyList<string>> _uninstallStudentsOwn;
    private string? _handledStudent;

    public PendingUninstallWorker(ProgramUninstaller uninstaller, PendingUninstalls pending)
        : this(() => UserSessionProcessLauncher.LoggedOnStudent()?.Student.Value, pending, uninstaller.UninstallStudentsOwn)
    {
    }

    // loggedOnStudent: the SID of the student at the console, or null.
    internal PendingUninstallWorker(
        Func<string?> loggedOnStudent, PendingUninstalls pending,
        Func<IReadOnlyList<string>, IReadOnlyList<string>> uninstallStudentsOwn)
    {
        _loggedOnStudent = loggedOnStudent;
        _pending = pending;
        _uninstallStudentsOwn = uninstallStudentsOwn;
    }

    // One look at the console. A student not seen since they logged on gets
    // the pending programs removed from their own; the same student again
    // gets nothing until they log off and back on, so an uninstaller that
    // fails isn't retried at every tick of their class.
    internal void RunOnce()
    {
        var student = _loggedOnStudent();
        if (student is null)
        {
            _handledStudent = null;
            return;
        }
        if (student == _handledStudent)
        {
            return;
        }

        _handledStudent = student;
        var names = _pending.Current();
        if (names.Count == 0)
        {
            return;
        }

        var removed = _uninstallStudentsOwn(names);
        if (removed.Count > 0)
        {
            AgentLog.Warn($"Removed at logon, as the teacher asked earlier: {string.Join(", ", removed)}.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var failing = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RunOnce();
                failing = false;
            }
            catch (Exception ex)
            {
                if (!failing)
                {
                    AgentLog.Warn($"Removing pending programs at logon failed: {ex.Message}");
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
