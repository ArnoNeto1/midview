using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class AppBlockEnforcerTests
{
    private readonly FakeInspector _inspector = new();
    private DateTime _now = new(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);

    private AppBlockEnforcer Create() => new(_inspector, () => _now);

    private static AppBlockPolicy BlockList(params string[] programs) => new(1, AppBlockMode.BlockList, programs);

    [Fact]
    public void AListedProgram_IsEnded_AndTheStudentIsToldOnce()
    {
        var enforcer = Create();
        enforcer.Apply(BlockList("chrome.exe"));
        var chrome = _inspector.Start("chrome.exe", "Google Chrome");
        _inspector.Start("winword.exe");

        Assert.Equal(1, enforcer.EnforceOnce());

        Assert.Equal(new[] { chrome }, _inspector.Ended);
        Assert.Equal(new[] { "O professor bloqueou este programa: Google Chrome." }, _inspector.Notices);
    }

    [Fact]
    public void AProgramStartedAgainAndAgain_IsEndedEachTime_ButTheNoticeIsThrottled()
    {
        var enforcer = Create();
        enforcer.Apply(BlockList("chrome.exe"));

        for (var i = 0; i < 5; i++)
        {
            _inspector.Start("chrome.exe", "Google Chrome");
            _now += TimeSpan.FromSeconds(1);
            enforcer.EnforceOnce();
        }

        Assert.Equal(5, _inspector.Ended.Count);
        Assert.Single(_inspector.Notices);

        _now += TimeSpan.FromSeconds(31);
        _inspector.Start("chrome.exe", "Google Chrome");
        enforcer.EnforceOnce();
        Assert.Equal(2, _inspector.Notices.Count);
    }

    [Fact]
    public void ANewPolicy_AlsoEndsWhatIsAlreadyOpen()
    {
        var enforcer = Create();
        _inspector.Start("chrome.exe");
        enforcer.Apply(BlockList("winword.exe"));
        enforcer.EnforceOnce();
        Assert.Empty(_inspector.Ended);

        enforcer.Apply(BlockList("chrome.exe"));
        enforcer.EnforceOnce();

        Assert.Single(_inspector.Ended);
    }

    [Fact]
    public void LiftedOrNoPolicy_DoesNothing()
    {
        var enforcer = Create();
        _inspector.Start("chrome.exe");

        Assert.Equal(0, enforcer.EnforceOnce());

        enforcer.Apply(BlockList("chrome.exe"));
        enforcer.Apply(AppBlockPolicy.Lifted(2));
        Assert.Equal(0, enforcer.EnforceOnce());
        Assert.Empty(_inspector.Ended);
    }

    [Fact]
    public void AProcessThatCantBeEnded_IsRetriedAfterAWhile_NotEveryTick()
    {
        var enforcer = Create();
        enforcer.Apply(BlockList("chrome.exe"));
        _inspector.Start("chrome.exe");
        _inspector.CanEnd = false;

        enforcer.EnforceOnce();
        enforcer.EnforceOnce();
        Assert.Equal(1, _inspector.EndAttempts);

        _now += TimeSpan.FromSeconds(31);
        _inspector.CanEnd = true;
        Assert.Equal(1, enforcer.EnforceOnce());
        Assert.Equal(2, _inspector.EndAttempts);
    }

    [Fact]
    public void AProcessThatVanishedBeforeBeingInspected_IsLeftAlone()
    {
        var enforcer = Create();
        enforcer.Apply(BlockList("chrome.exe"));
        _inspector.Start("chrome.exe", gone: true);

        Assert.Equal(0, enforcer.EnforceOnce());
        Assert.Empty(_inspector.Ended);
    }

    [Fact]
    public void AProcessThatCantBeToldAboutYet_IsLookedAtAgainSoon_NotLeftAloneForGood()
    {
        var enforcer = Create();
        enforcer.Apply(BlockList("chrome.exe"));
        var chrome = _inspector.Start("chrome.exe", gone: true);

        Assert.Equal(0, enforcer.EnforceOnce());
        _inspector.Reveal(chrome, "chrome.exe");

        // Not yet: it was just looked at.
        Assert.Equal(0, enforcer.EnforceOnce());

        _now += TimeSpan.FromSeconds(3);
        Assert.Equal(1, enforcer.EnforceOnce());
        Assert.Equal(new[] { chrome }, _inspector.Ended);
    }

    [Fact]
    public void ALiftWhileProcessesAreBeingInspected_EndsNothingMore()
    {
        var enforcer = Create();
        enforcer.Apply(BlockList("chrome.exe"));
        _inspector.Start("chrome.exe");
        _inspector.Start("chrome.exe");
        _inspector.OnInspect = () => enforcer.Apply(AppBlockPolicy.Lifted(2));

        Assert.Equal(0, enforcer.EnforceOnce());

        Assert.Empty(_inspector.Ended);
        Assert.Equal(0, enforcer.EnforceOnce());
    }

    [Fact]
    public void ANewPolicyWhileProcessesAreBeingInspected_IsAppliedToEverythingOnTheNextTick()
    {
        var enforcer = Create();
        enforcer.Apply(BlockList("winword.exe"));
        _inspector.Start("chrome.exe");
        _inspector.OnInspect = () =>
        {
            _inspector.OnInspect = null;
            enforcer.Apply(BlockList("chrome.exe"));
        };

        // The decision taken under the old policy is dropped...
        Assert.Equal(0, enforcer.EnforceOnce());
        // ...and the process is looked at again under the new one.
        Assert.Equal(1, enforcer.EnforceOnce());
    }

    [Fact]
    public void ANoticeThatFails_NeverStopsEnforcing()
    {
        var enforcer = Create();
        enforcer.Apply(BlockList("chrome.exe"));
        _inspector.NoticeThrows = true;
        _inspector.Start("chrome.exe");

        Assert.Equal(1, enforcer.EnforceOnce());
    }

    private sealed class FakeInspector : IProcessInspector
    {
        private readonly Dictionary<RunningProcess, ProcessFacts?> _processes = new();
        private int _nextId = 100;

        public List<RunningProcess> Ended { get; } = new();
        public List<string> Notices { get; } = new();
        public bool CanEnd { get; set; } = true;
        public bool NoticeThrows { get; set; }
        public int EndAttempts { get; private set; }

        public RunningProcess Start(string imageName, string? displayName = null, bool gone = false)
        {
            var process = new RunningProcess(_nextId++, _nextId, SessionId: 1);
            _processes[process] = gone
                ? null
                : new ProcessFacts(process.ProcessId, DateTime.UnixEpoch, 1, true, false, imageName, null, false, true,
                    FileOwnerKind.Trusted, displayName ?? imageName);
            return process;
        }

        // What Inspect couldn't tell at first (nobody logged on yet, say) becomes known.
        public void Reveal(RunningProcess process, string imageName) =>
            _processes[process] = new ProcessFacts(process.ProcessId, DateTime.UnixEpoch, 1, true, false, imageName, null, false, true,
                FileOwnerKind.Trusted, imageName);

        public Action? OnInspect { get; set; }

        public IReadOnlyList<RunningProcess> ListInteractive() =>
            _processes.Keys.Where(process => !Ended.Contains(process)).ToList();

        public ProcessFacts? Inspect(RunningProcess process)
        {
            OnInspect?.Invoke();
            return _processes[process];
        }

        public bool End(RunningProcess process)
        {
            EndAttempts++;
            if (!CanEnd)
            {
                return false;
            }
            Ended.Add(process);
            return true;
        }

        public void Notify(int sessionId, string message)
        {
            if (NoticeThrows)
            {
                throw new InvalidOperationException("sem sessão");
            }
            Notices.Add(message);
        }
    }
}
