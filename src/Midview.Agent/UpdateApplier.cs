using Midview.Protocol;

namespace Midview.Agent;

// Applies a received update (Midview.Agent.exe --apply-update) and undoes an
// interrupted one at boot (--recover-update). Runs from the build that is
// already installed and working -- never from the one it switches to -- as
// SYSTEM, as its own process: stopping the service doesn't stop it.
//
// Nothing is copied, moved or deleted here: both builds are complete
// folders, and applying or rolling back is switching the service's binary
// path. The rule every path keeps: the service ends up pointing at a
// complete build and running, and an attempt that wasn't confirmed ends on
// the old build. The boot task goes away only once a final state is on
// disk: until then, the next boot finishes the job.
public sealed class UpdateApplier
{
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollStep = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan KillGrace = TimeSpan.FromSeconds(15);

    private readonly UpdatePaths _paths;
    private readonly IServiceControl _service;
    private readonly IRecoveryTask _recoveryTask;
    private readonly Action _killSessions;
    private readonly IUpdateClock _clock;

    public UpdateApplier(
        UpdatePaths paths, IServiceControl service, IRecoveryTask recoveryTask, Action killSessions, IUpdateClock clock)
    {
        _paths = paths;
        _service = service;
        _recoveryTask = recoveryTask;
        _killSessions = killSessions;
        _clock = clock;
    }

    // The receiver hands over an attempt in the "verifying" state with both
    // binaries recorded; anything else isn't ours to apply.
    public int Apply()
    {
        using var updateLock = UpdateLock.Acquire(_paths.LockPath, LockWait, _clock);
        if (updateLock is null)
        {
            AgentLog.Warn("Update applier: the update lock is held by someone else; not applying.");
            return 1;
        }

        var record = UpdateRecordFile.Load(_paths.StatePath);
        if (record is not { State: UpdateStates.Verifying, OldBinary: { } oldBinary, NewBinary: { } newBinary })
        {
            AgentLog.Warn($"Update applier: nothing to apply (state {record?.State ?? "none"}).");
            return 1;
        }

        if (!File.Exists(oldBinary) || !File.Exists(newBinary))
        {
            TrySave(ref record, UpdateStates.Failed, "os arquivos da atualização não estão mais lá");
            return 1;
        }

        // The receiver takes "applying" as the sign this process owns the
        // attempt; without it on disk, nothing is switched.
        if (!TrySave(ref record, UpdateStates.Applying, "aplicando"))
        {
            return 1;
        }
        UpdateConfirmation.Delete(_paths.ConfirmationPath);

        var switched = false;
        try
        {
            _recoveryTask.Register(oldBinary);
            _service.SetBinaryPath(newBinary);
            switched = true;

            // The new build only confirms a provisional attempt.
            if (!TrySave(ref record, UpdateStates.Provisional, "reiniciando na versão nova"))
            {
                RollBack(record, oldBinary, "o registro da atualização não pôde ser gravado");
                return 1;
            }

            var processId = Restart();
            if (processId == 0)
            {
                RollBack(record, oldBinary, "a versão nova não iniciou");
                return 0;
            }

            var outcome = WaitForConfirmation(record.AttemptId, processId);
            if (outcome is null)
            {
                Commit(record);
            }
            else
            {
                RollBack(record, oldBinary, outcome);
            }
            return 0;
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Update applier failed: {ex.Message}");
            if (switched)
            {
                RollBack(record, oldBinary, $"erro ao aplicar: {ErrorText.Describe(ex)}");
            }
            else if (TrySave(ref record, UpdateStates.Failed, $"erro ao aplicar: {ErrorText.Describe(ex)}"))
            {
                TryDeleteRecoveryTask();
            }
            return 1;
        }
        finally
        {
            EnsureStarted();
        }
    }

    // At boot, from the old build: an attempt that didn't reach a final
    // state was interrupted (power cut, shutdown, crash) or couldn't switch
    // back -- back to the old build. The task deletes itself once the
    // attempt is final on disk; if this fails, the next boot tries again.
    public int Recover()
    {
        using var updateLock = UpdateLock.Acquire(_paths.LockPath, TimeSpan.FromMinutes(1), _clock);
        var record = UpdateRecordFile.Load(_paths.StatePath);
        var done = true;
        try
        {
            if (record is not null && !UpdateStates.IsFinal(record.State))
            {
                if (record.OldBinary is { } oldBinary && File.Exists(oldBinary))
                {
                    _service.SetBinaryPath(oldBinary);
                    Restart();
                    done = TrySave(ref record, UpdateStates.RolledBack, "interrompida; voltou para a versão anterior");
                }
                else
                {
                    done = TrySave(ref record, UpdateStates.Failed, "interrompida");
                }
            }
            return done ? 0 : 1;
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Update recovery failed: {ex.Message}");
            done = false;
            return 1;
        }
        finally
        {
            if (done)
            {
                TryDeleteRecoveryTask();
            }
            EnsureStarted();
        }
    }

    // Null when the new build was confirmed in time on the same process;
    // otherwise why not.
    private string? WaitForConfirmation(string attemptId, int processId)
    {
        var deadline = _clock.Now + NetworkUpdate.ConfirmationWindow;
        while (_clock.Now < deadline)
        {
            var snapshot = _service.Query();
            if (snapshot.State is ServiceRunState.Stopped or ServiceRunState.StopPending || snapshot.ProcessId != processId)
            {
                return "a versão nova parou ou reiniciou sozinha";
            }
            if (UpdateConfirmation.Read(_paths.ConfirmationPath) == attemptId)
            {
                return null;
            }
            _clock.Sleep(PollStep);
        }
        return "a versão nova não foi confirmada pelo MidviewMaster a tempo";
    }

    // A commit that can't be recorded keeps the boot task: the next boot
    // then goes back to the old build rather than leave an attempt that
    // looks unfinished forever.
    private void Commit(UpdateRecord record)
    {
        if (TrySave(ref record, UpdateStates.Committed, record.TargetBuild is { } build ? $"atualizada para {build}" : "atualizada"))
        {
            UpdateConfirmation.Delete(_paths.ConfirmationPath);
            TryDeleteRecoveryTask();
        }
    }

    // If switching back fails, the attempt stays unfinished (RollbackPending)
    // with the boot task in place, so the next boot switches back.
    private void RollBack(UpdateRecord record, string oldBinary, string reason)
    {
        try
        {
            _service.SetBinaryPath(oldBinary);
            var processId = Restart();
            var healthy = processId != 0 && StaysUp(processId);
            if (TrySave(ref record, UpdateStates.RolledBack, healthy
                    ? $"{reason}; voltou para a versão anterior"
                    : $"{reason}; voltou para a versão anterior, mas ela também não ficou de pé (veja a máquina)"))
            {
                UpdateConfirmation.Delete(_paths.ConfirmationPath);
                TryDeleteRecoveryTask();
            }
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Update rollback failed: {ex.Message}");
            TrySave(ref record, UpdateStates.RollbackPending,
                $"{reason}; e não foi possível voltar para a versão anterior agora ({ErrorText.Describe(ex)}) — ela volta ao reiniciar a máquina");
        }
    }

    private bool StaysUp(int processId)
    {
        var until = _clock.Now + NetworkUpdate.RollbackCheck;
        while (_clock.Now < until)
        {
            var snapshot = _service.Query();
            if (snapshot.State != ServiceRunState.Running || snapshot.ProcessId != processId)
            {
                return false;
            }
            _clock.Sleep(PollStep);
        }
        return true;
    }

    // Stop through the SCM and wait for the process to go (killed after the
    // timeout); end every Session; start. The binary path was switched
    // before, so an SCM restart-on-failure in between can only start a
    // complete build -- the same one we start. Returns the new process id,
    // or 0 if it didn't reach Running in time.
    private int Restart()
    {
        var before = _service.Query();
        _service.RequestStop();
        if (!WaitFor(snapshot => snapshot.State == ServiceRunState.Stopped, NetworkUpdate.ServiceStopTimeout)
            && before.ProcessId != 0)
        {
            _service.KillProcess(before.ProcessId);
            WaitFor(snapshot => snapshot.State == ServiceRunState.Stopped || snapshot.ProcessId != before.ProcessId, KillGrace);
        }

        _killSessions();
        _service.RequestStart();

        ServiceSnapshot running = default;
        return WaitFor(snapshot =>
            {
                running = snapshot;
                return snapshot is { State: ServiceRunState.Running, ProcessId: not 0 };
            }, NetworkUpdate.ServiceStartTimeout)
            ? running.ProcessId
            : 0;
    }

    private bool WaitFor(Func<ServiceSnapshot, bool> condition, TimeSpan timeout)
    {
        var deadline = _clock.Now + timeout;
        while (true)
        {
            if (condition(_service.Query()))
            {
                return true;
            }
            if (_clock.Now >= deadline)
            {
                return false;
            }
            _clock.Sleep(PollStep);
        }
    }

    private void EnsureStarted()
    {
        try
        {
            _service.RequestStart();
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Couldn't start the service after an update step: {ex.Message}");
        }
    }

    private void TryDeleteRecoveryTask()
    {
        try
        {
            _recoveryTask.Delete();
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Couldn't delete the update recovery task: {ex.Message}");
        }
    }

    // False when the state couldn't be written: what's on disk is still the
    // previous state, and the caller must act on that.
    private bool TrySave(ref UpdateRecord record, string state, string detail)
    {
        var updated = record with { State = state, Detail = detail, UpdatedAt = _clock.Now };
        try
        {
            UpdateRecordFile.Save(_paths.StatePath, updated);
            record = updated;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Warn($"Couldn't save the update record ({state}): {ex.Message}");
            return false;
        }
    }
}
