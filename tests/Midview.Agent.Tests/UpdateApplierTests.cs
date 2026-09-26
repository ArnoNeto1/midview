using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class UpdateApplierTests : IDisposable
{
    private const string AttemptId = "00112233445566778899AABBCCDDEEFF";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-apply-{Guid.NewGuid():N}");
    private readonly UpdatePaths _paths;
    private readonly string _oldBinary;
    private readonly string _newBinary;
    private readonly FakeService _service = new();
    private readonly FakeRecoveryTask _task = new();
    private readonly FakeClock _clock = new();
    private int _sessionKills;

    public UpdateApplierTests()
    {
        _paths = new UpdatePaths(Path.Combine(_root, "Midview.update"), Path.Combine(_root, "data"));
        _oldBinary = CreateExe(Path.Combine(_root, "Midview"));
        _newBinary = CreateExe(_paths.BuildFolder(AttemptId));
        _service.BinaryPath = _oldBinary;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private UpdateApplier Applier() => new(_paths, _service, _task, () => _sessionKills++, _clock);

    private void HandOver(string state = UpdateStates.Verifying) =>
        UpdateRecordFile.Save(_paths.StatePath, new UpdateRecord(
            AttemptId, "FP", "2026-09-26 19:40 a1b2c3d", state, "", _clock.Now, _oldBinary, _newBinary));

    private UpdateRecord Record() => UpdateRecordFile.Load(_paths.StatePath)!;

    // What the new build does when the Master polls it: confirm its attempt.
    private void NewBuildConfirmsOnceRunning() => _service.OnQuery = service =>
    {
        if (service.BinaryPath == _newBinary && service.State == ServiceRunState.Running)
        {
            UpdateConfirmation.Write(_paths.ConfirmationPath, AttemptId);
        }
    };

    [Fact]
    public void Confirmed_Commits_OnTheNewBuild()
    {
        HandOver();
        NewBuildConfirmsOnceRunning();

        Assert.Equal(0, Applier().Apply());

        Assert.Equal(UpdateStates.Committed, Record().State);
        Assert.Equal("atualizada para 2026-09-26 19:40 a1b2c3d", Record().Detail);
        Assert.Equal(_newBinary, _service.BinaryPath);
        Assert.Equal(ServiceRunState.Running, _service.State);
        Assert.Equal(_oldBinary, _task.Registered);
        Assert.True(_task.Deleted);
        Assert.Equal(1, _sessionKills);
        Assert.Equal(new[] { _newBinary }, _service.StartedBinaries);
    }

    [Fact]
    public void TheBinaryPath_IsSwitchedBeforeTheServiceStops()
    {
        HandOver();
        NewBuildConfirmsOnceRunning();

        Applier().Apply();

        // An SCM restart in between can then only start a complete build.
        Assert.True(_service.Calls.IndexOf($"path {_newBinary}") < _service.Calls.IndexOf("stop"));
        Assert.True(_task.RegisteredBeforePathSwitch(_service));
    }

    [Fact]
    public void NotConfirmed_RollsBack_ToTheOldBuild()
    {
        HandOver();

        Applier().Apply();

        Assert.Equal(UpdateStates.RolledBack, Record().State);
        Assert.Contains("não foi confirmada", Record().Detail);
        Assert.Equal(_oldBinary, _service.BinaryPath);
        Assert.Equal(new[] { _newBinary, _oldBinary }, _service.StartedBinaries);
        Assert.True(_task.Deleted);
    }

    [Fact]
    public void AConfirmationForAnotherAttempt_DoesNotCommit()
    {
        HandOver();
        UpdateConfirmation.Write(_paths.ConfirmationPath, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");

        Applier().Apply();

        Assert.Equal(UpdateStates.RolledBack, Record().State);
    }

    [Fact]
    public void ANewBuildThatCrashes_RollsBack()
    {
        HandOver();
        // It comes up, then the SCM restarts it after a crash: a new process.
        var queries = 0;
        _service.OnQuery = service =>
        {
            if (service.BinaryPath == _newBinary && service.State == ServiceRunState.Running && ++queries == 3)
            {
                service.CrashAndRestart();
            }
        };

        Applier().Apply();

        Assert.Equal(UpdateStates.RolledBack, Record().State);
        Assert.Contains("parou ou reiniciou", Record().Detail);
        Assert.Equal(_oldBinary, _service.BinaryPath);
    }

    [Fact]
    public void ANewBuildThatNeverStarts_RollsBack()
    {
        HandOver();
        _service.NeverStartsFrom = _newBinary;

        Applier().Apply();

        Assert.Equal(UpdateStates.RolledBack, Record().State);
        Assert.Contains("não iniciou", Record().Detail);
        Assert.Equal(_oldBinary, _service.BinaryPath);
        Assert.Equal(ServiceRunState.Running, _service.State);
    }

    [Fact]
    public void AServiceThatWontStop_IsKilledAfterTheTimeout()
    {
        HandOver();
        NewBuildConfirmsOnceRunning();
        _service.RefuseToStop = true;

        Applier().Apply();

        Assert.Contains("kill 100", _service.Calls);
        Assert.Equal(UpdateStates.Committed, Record().State);
    }

    [Fact]
    public void NothingHandedOver_TouchesNothing()
    {
        HandOver(UpdateStates.Committed);

        Assert.Equal(1, Applier().Apply());

        Assert.DoesNotContain(_service.Calls, call => call != "start");
        Assert.Null(_task.Registered);
    }

    [Fact]
    public void TheLockHeldElsewhere_TouchesNothing()
    {
        HandOver();
        using var held = UpdateLock.TryAcquire(_paths.LockPath);

        Assert.Equal(1, Applier().Apply());

        Assert.Equal(UpdateStates.Verifying, Record().State);
        Assert.Equal(_oldBinary, _service.BinaryPath);
    }

    [Fact]
    public void TheRecoveryTaskFailing_FailsBeforeSwitchingAnything()
    {
        HandOver();
        _task.ThrowOnRegister = true;

        Assert.Equal(1, Applier().Apply());

        Assert.Equal(UpdateStates.Failed, Record().State);
        Assert.Equal(_oldBinary, _service.BinaryPath);
        Assert.Equal(ServiceRunState.Running, _service.State);
    }

    [Fact]
    public void ARollbackThatCantSwitchBack_IsLeftToTheBootTask_WhichSwitchesBack()
    {
        HandOver();
        _service.SetPathThrows = path => path == _oldBinary ? new InvalidOperationException("SCM ocupado") : null;

        Applier().Apply();

        Assert.Equal(UpdateStates.RollbackPending, Record().State);
        Assert.Contains("ao reiniciar a máquina", Record().Detail);
        Assert.False(_task.Deleted);

        // The next boot.
        _service.SetPathThrows = null;
        Assert.Equal(0, Applier().Recover());

        Assert.Equal(UpdateStates.RolledBack, Record().State);
        Assert.Equal(_oldBinary, _service.BinaryPath);
        Assert.True(_task.Deleted);
    }

    [Fact]
    public void AnApplyingStateThatCantBeSaved_SwitchesNothing()
    {
        HandOver();
        BlockRecordWrites();

        Assert.Equal(1, Applier().Apply());

        Assert.DoesNotContain(_service.Calls, call => call.StartsWith("path"));
        Assert.Null(_task.Registered);
        Assert.Equal(UpdateStates.Verifying, Record().State);
    }

    [Fact]
    public void ACommitThatCantBeSaved_KeepsTheBootTask()
    {
        HandOver();
        _service.OnQuery = service =>
        {
            if (service.BinaryPath == _newBinary && service.State == ServiceRunState.Running
                && UpdateConfirmation.Read(_paths.ConfirmationPath) is null)
            {
                UpdateConfirmation.Write(_paths.ConfirmationPath, AttemptId);
                BlockRecordWrites();
            }
        };

        Applier().Apply();

        // Still unfinished on disk, so the boot task must stay to finish it.
        Assert.Equal(UpdateStates.Provisional, Record().State);
        Assert.False(_task.Deleted);
    }

    [Fact]
    public void Recover_ThatCantSwitchBack_KeepsTheTask_ForTheNextBoot()
    {
        HandOver(UpdateStates.Provisional);
        _service.BinaryPath = _newBinary;
        _task.Registered = _oldBinary;
        _service.SetPathThrows = _ => new InvalidOperationException("SCM ocupado");

        Assert.Equal(1, Applier().Recover());

        Assert.Equal(UpdateStates.Provisional, Record().State);
        Assert.False(_task.Deleted);
    }

    [Fact]
    public void Recover_AnInterruptedAttempt_GoesBackToTheOldBuild()
    {
        HandOver(UpdateStates.Provisional);
        _service.BinaryPath = _newBinary;
        _task.Registered = _oldBinary;

        Assert.Equal(0, Applier().Recover());

        Assert.Equal(UpdateStates.RolledBack, Record().State);
        Assert.Contains("interrompida", Record().Detail);
        Assert.Equal(_oldBinary, _service.BinaryPath);
        Assert.True(_task.Deleted);
    }

    [Fact]
    public void Recover_AFinishedAttempt_OnlyDeletesTheTask()
    {
        HandOver(UpdateStates.Committed);
        _service.BinaryPath = _newBinary;

        Applier().Recover();

        Assert.Equal(UpdateStates.Committed, Record().State);
        Assert.Equal(_newBinary, _service.BinaryPath);
        Assert.True(_task.Deleted);
    }

    // The record can still be read, but no new state can be written: the
    // temp file it's written through can't be created.
    private void BlockRecordWrites() => Directory.CreateDirectory(_paths.StatePath + ".tmp");

    private static string CreateExe(string folder)
    {
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, UpdatePaths.AgentExeName);
        File.WriteAllText(exe, "exe");
        return exe;
    }

    internal sealed class FakeClock : IUpdateClock
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 9, 26, 22, 0, 0, TimeSpan.FromHours(-3));

        public void Sleep(TimeSpan duration) => Now += duration;
    }

    private sealed class FakeRecoveryTask : IRecoveryTask
    {
        public string? Registered { get; set; }
        public bool Deleted { get; private set; }
        public bool ThrowOnRegister { get; set; }
        public int RegisterOrder { get; private set; } = -1;

        public void Register(string oldAgentExe)
        {
            if (ThrowOnRegister)
            {
                throw new IOException("schtasks recusou");
            }
            Registered = oldAgentExe;
            RegisterOrder = FakeService.Clock++;
        }

        public void Delete() => Deleted = true;

        public bool RegisteredBeforePathSwitch(FakeService service) => RegisterOrder < service.FirstPathSwitchOrder;
    }

    // A service as the SCM shows it: stop and start take effect at once
    // unless told otherwise; each start is a new process.
    internal sealed class FakeService : IServiceControl
    {
        public static int Clock;

        private int _nextProcessId = 200;

        public string? BinaryPath { get; set; }
        public ServiceRunState State { get; private set; } = ServiceRunState.Running;
        public int ProcessId { get; private set; } = 100;
        public List<string> Calls { get; } = new();
        public List<string?> StartedBinaries { get; } = new();
        public bool RefuseToStop { get; set; }
        public string? NeverStartsFrom { get; set; }
        public Action<FakeService>? OnQuery { get; set; }
        public Func<string, Exception?>? SetPathThrows { get; set; }
        public int FirstPathSwitchOrder { get; private set; } = int.MaxValue;

        public string? GetBinaryPath() => BinaryPath;

        public void SetBinaryPath(string exePath)
        {
            Calls.Add($"path {exePath}");
            FirstPathSwitchOrder = Math.Min(FirstPathSwitchOrder, Clock++);
            if (SetPathThrows?.Invoke(exePath) is { } exception)
            {
                throw exception;
            }
            BinaryPath = exePath;
        }

        public ServiceSnapshot Query()
        {
            OnQuery?.Invoke(this);
            return new ServiceSnapshot(State, State == ServiceRunState.Stopped ? 0 : ProcessId);
        }

        public void RequestStop()
        {
            Calls.Add("stop");
            if (!RefuseToStop)
            {
                State = ServiceRunState.Stopped;
            }
        }

        public void RequestStart()
        {
            Calls.Add("start");
            if (State == ServiceRunState.Stopped && BinaryPath != NeverStartsFrom)
            {
                State = ServiceRunState.Running;
                ProcessId = _nextProcessId++;
                StartedBinaries.Add(BinaryPath);
            }
        }

        public void KillProcess(int processId)
        {
            Calls.Add($"kill {processId}");
            if (processId == ProcessId)
            {
                State = ServiceRunState.Stopped;
            }
        }

        public void CrashAndRestart() => ProcessId = _nextProcessId++;
    }
}
