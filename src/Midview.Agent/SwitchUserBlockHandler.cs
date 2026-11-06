using Midview.Protocol;

namespace Midview.Agent;

// Closes the Ctrl+Alt+Del escapes from a locked screen while any teacher holds
// a HoldSwitchUserBlock connection -- that is, while the machine's screen is
// locked: "Switch user" (HKLM HideFastUserSwitching, RegistrySwitchUserPolicy)
// and the student's Task Manager, sign out, lock, change password and power (their
// own hive, StudentCtrlAltDelLockdown). Ref-counted across teachers, so two locks block
// once and it all comes back only when the last lock ends. It rides on the
// Agent's SYSTEM rights, which the user-level Session doesn't have for these
// policies. Each policy is applied on its own: one that fails is logged and
// leaves the others working.
public sealed class SwitchUserBlockHandler
{
    private readonly IReadOnlyList<ISwitchUserPolicy> _policies;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private int _holds;

    public SwitchUserBlockHandler(ISwitchUserPolicy policy)
        : this(new[] { policy }, AgentLog.Warn)
    {
    }

    public SwitchUserBlockHandler(IReadOnlyList<ISwitchUserPolicy> policies, Action<string> log)
    {
        _policies = policies;
        _log = log;
        // A block left set by a crash has no live connection holding it now:
        // clear it, so switching user isn't hidden outside a lock.
        Each(policy => policy.Unblock());
    }

    // Keeps the block for as long as the connection stays open. The Master's
    // heartbeats keep it up; any drop, or the receive timeout, throws out of
    // receiveNext and frees the hold.
    public void Hold(Func<Message> receiveNext)
    {
        Acquire();
        try
        {
            while (true)
            {
                receiveNext();
            }
        }
        catch
        {
            // The connection closed or timed out: fall through to release.
        }
        finally
        {
            Release();
        }
    }

    internal void Acquire()
    {
        lock (_gate)
        {
            if (_holds++ == 0)
            {
                Each(policy => policy.Block());
            }
        }
    }

    internal void Release()
    {
        lock (_gate)
        {
            if (--_holds == 0)
            {
                Each(policy => policy.Unblock());
            }
        }
    }

    private void Each(Action<ISwitchUserPolicy> apply)
    {
        foreach (var policy in _policies)
        {
            try
            {
                apply(policy);
            }
            catch (Exception ex)
            {
                _log($"Screen lock policy {policy.GetType().Name}: {ex.Message}");
            }
        }
    }
}
