using Midview.Protocol;

namespace Midview.Agent;

// A process in an interactive session, told apart from a later process
// that reuses its id by its creation time (a FILETIME).
public readonly record struct RunningProcess(int ProcessId, long CreationTime, int SessionId);

// The Windows side of program blocking (WindowsProcessInspector), apart so
// the enforcer can be tested with a fake.
public interface IProcessInspector
{
    // Oldest first, so a program is judged before the ones it starts.
    IReadOnlyList<RunningProcess> ListInteractive();

    // Null when the process can't be told about right now (it is gone, can't
    // be read, or nobody is logged on to its session yet): it isn't ended,
    // and is looked at again shortly.
    ProcessFacts? Inspect(RunningProcess process);

    // Only that process (same id and creation time); false if it couldn't
    // be ended.
    bool End(RunningProcess process);

    void Notify(int sessionId, string message);
}

// Keeps the program-blocking policy enforced: every tick, each process not
// decided before is checked against the policy (AppBlockRules) and ended if
// the policy forbids it. A new policy checks everything again, so what is
// already open is ended too.
//
// Slow work (listing and inspecting processes) happens outside the lock; the
// lock is held to look at the policy, to end a process and to record the
// decision, and a decision made under an older policy is dropped. So once
// Apply has returned -- a lift included -- nothing is ended under the policy
// it replaced.
public sealed class AppBlockEnforcer
{
    private static readonly TimeSpan EndFailedRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UndecidedRetry = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NoticeInterval = TimeSpan.FromSeconds(30);

    private readonly IProcessInspector _inspector;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();

    // Decided processes; a value is when to look at one again (one that
    // couldn't be ended, or that couldn't be told about), null for a final
    // decision.
    private readonly Dictionary<RunningProcess, DateTime?> _seen = new();
    private readonly Dictionary<(int SessionId, string Program), DateTime> _lastNotice = new();
    private AppBlockPolicy _policy = AppBlockPolicy.Lifted(0);
    private long _generation;

    public AppBlockEnforcer(IProcessInspector inspector, Func<DateTime>? utcNow = null)
    {
        _inspector = inspector;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public void Apply(AppBlockPolicy policy)
    {
        lock (_gate)
        {
            _policy = policy;
            _generation++;
            _seen.Clear();
        }
    }

    // Returns how many processes were ended.
    public int EnforceOnce()
    {
        AppBlockPolicy policy;
        long generation;
        lock (_gate)
        {
            policy = _policy;
            generation = _generation;
            if (!policy.IsActive)
            {
                _seen.Clear();
                _lastNotice.Clear();
                return 0;
            }
        }

        var now = _utcNow();
        var running = _inspector.ListInteractive();

        List<RunningProcess> toInspect;
        lock (_gate)
        {
            if (generation != _generation)
            {
                return 0;
            }

            var present = running.ToHashSet();
            foreach (var gone in _seen.Keys.Where(process => !present.Contains(process)).ToList())
            {
                _seen.Remove(gone);
            }
            toInspect = running
                .Where(process => !_seen.TryGetValue(process, out var retryAt) || (retryAt is not null && retryAt <= now))
                .ToList();
        }

        var ended = 0;
        var notices = new List<(int SessionId, string Message)>();
        var log = new List<string>();
        foreach (var process in toInspect)
        {
            var facts = _inspector.Inspect(process);
            var forbidden = facts is not null && AppBlockRules.ShouldEnd(policy, facts);

            lock (_gate)
            {
                if (generation != _generation)
                {
                    // The policy changed while looking: this decision is
                    // stale, and the next tick looks at everything again.
                    break;
                }

                if (facts is null)
                {
                    _seen[process] = now + UndecidedRetry;
                    continue;
                }
                if (!forbidden)
                {
                    _seen[process] = null;
                    continue;
                }

                if (_inspector.End(process))
                {
                    ended++;
                    _seen[process] = null;
                    log.Add($"Program blocking ended {facts.ImageName} (process {process.ProcessId}).");
                    if (NoticeDue(process.SessionId, facts.ImageName, now))
                    {
                        notices.Add((process.SessionId, $"O professor bloqueou este programa: {facts.DisplayName}."));
                    }
                }
                else
                {
                    _seen[process] = now + EndFailedRetry;
                    log.Add($"Program blocking couldn't end {facts.ImageName} (process {process.ProcessId}).");
                }
            }
        }

        // The log and the student's session are outside the lock: they are I/O.
        foreach (var line in log)
        {
            AgentLog.Warn(line);
        }
        foreach (var (sessionId, message) in notices)
        {
            try
            {
                _inspector.Notify(sessionId, message);
            }
            catch (Exception ex)
            {
                AgentLog.Warn($"Couldn't tell the student a program was blocked: {ex.Message}");
            }
        }
        return ended;
    }

    // At most one notice per program and session every NoticeInterval, so a
    // program that is started again and again doesn't flood the screen. Old
    // entries are dropped so the table can't grow without end.
    private bool NoticeDue(int sessionId, string program, DateTime now)
    {
        foreach (var old in _lastNotice.Where(entry => now - entry.Value >= NoticeInterval).Select(entry => entry.Key).ToList())
        {
            _lastNotice.Remove(old);
        }

        var key = (sessionId, program);
        if (_lastNotice.ContainsKey(key))
        {
            return false;
        }
        _lastNotice[key] = now;
        return true;
    }
}
