using Midview.Protocol;

namespace Midview.Agent;

// Hides "Switch user" (HKLM HideFastUserSwitching) while any teacher holds a
// HoldSwitchUserBlock connection -- that is, while the machine's screen is
// locked. Ref-counted across teachers, so two locks hide it once and it comes
// back only when the last lock ends. The block rides on the Agent's SYSTEM
// rights, which the user-level Session can't use for an HKLM policy.
public sealed class SwitchUserBlockHandler
{
    private readonly ISwitchUserPolicy _policy;
    private readonly object _gate = new();
    private int _holds;

    public SwitchUserBlockHandler(ISwitchUserPolicy policy)
    {
        _policy = policy;
        // A block left set by a crash has no live connection holding it now:
        // clear it, so switching user isn't hidden outside a lock.
        _policy.Unblock();
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
                _policy.Block();
            }
        }
    }

    internal void Release()
    {
        lock (_gate)
        {
            if (--_holds == 0)
            {
                _policy.Unblock();
            }
        }
    }
}
