using Midview.Protocol;

namespace Midview.Agent;

// A process in an interactive session, told apart from a later process
// that reuses its id by its creation time (a FILETIME).
public readonly record struct RunningProcess(int ProcessId, long CreationTime, int SessionId);

// The Windows side of program blocking (WindowsProcessInspector), apart so
// the enforcer can be tested with a fake.
public interface IProcessInspector
{
    IReadOnlyList<RunningProcess> ListInteractive();

    // Null when the process is gone or can't be read: it's left alone.
    ProcessFacts? Inspect(RunningProcess process);

    // Only that process (same id and creation time); false if it couldn't
    // be ended.
    bool End(RunningProcess process);

    void Notify(int sessionId, string message);
}

// Keeps the program-blocking policy enforced: every tick, each process not
// seen before is checked against the policy (AppBlockRules) and ended if the
// policy forbids it. A new policy checks everything again, so what is
// already open is ended too.
public sealed class AppBlockEnforcer
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NoticeInterval = TimeSpan.FromSeconds(30);

    private readonly IProcessInspector _inspector;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();

    // Decided processes; a value is when to try again to end one that
    // couldn't be ended.
    private readonly Dictionary<RunningProcess, DateTime?> _seen = new();
    private readonly Dictionary<(int SessionId, string Program), DateTime> _lastNotice = new();
    private AppBlockPolicy _policy = AppBlockPolicy.Lifted(0);

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
            _seen.Clear();
        }
    }

    // Returns how many processes were ended.
    public int EnforceOnce()
    {
        lock (_gate)
        {
            if (!_policy.IsActive)
            {
                _seen.Clear();
                return 0;
            }

            var now = _utcNow();
            var running = _inspector.ListInteractive();
            var present = running.ToHashSet();
            foreach (var gone in _seen.Keys.Where(process => !present.Contains(process)).ToList())
            {
                _seen.Remove(gone);
            }

            var ended = 0;
            foreach (var process in running)
            {
                if (_seen.TryGetValue(process, out var retryAt) && (retryAt is null || retryAt > now))
                {
                    continue;
                }

                var facts = _inspector.Inspect(process);
                if (facts is null || !AppBlockRules.ShouldEnd(_policy, facts))
                {
                    _seen[process] = null;
                    continue;
                }

                if (_inspector.End(process))
                {
                    ended++;
                    _seen[process] = null;
                    AgentLog.Warn($"Program blocking ended {facts.ImageName} (process {process.ProcessId}).");
                    NotifyAtMostEvery(NoticeInterval, process.SessionId, facts, now);
                }
                else
                {
                    _seen[process] = now + RetryAfter;
                    AgentLog.Warn($"Program blocking couldn't end {facts.ImageName} (process {process.ProcessId}).");
                }
            }
            return ended;
        }
    }

    private void NotifyAtMostEvery(TimeSpan interval, int sessionId, ProcessFacts facts, DateTime now)
    {
        var key = (sessionId, facts.ImageName);
        if (_lastNotice.TryGetValue(key, out var last) && now - last < interval)
        {
            return;
        }
        _lastNotice[key] = now;
        try
        {
            _inspector.Notify(sessionId, $"O professor bloqueou este programa: {facts.DisplayName}.");
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Couldn't tell the student a program was blocked: {ex.Message}");
        }
    }
}
