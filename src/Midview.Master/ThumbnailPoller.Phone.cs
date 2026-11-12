using Midview.Protocol;

namespace Midview.Master;

// A phone's chores, done right after its thumbnail like a PC Agent's: the
// class's app block for phones, handed over whenever it changes, and what the
// phone follows, for its tile. They use the program block's bookkeeping
// (_sentAppBlockSavedAt...): a poller is for a phone or for a PC, never both.
public sealed partial class ThumbnailPoller
{
    // What app block the phone says it follows, for the tile's badge: every
    // so often while the phone is on. Null when it didn't answer.
    public event Action<PhoneAppBlockClient.StatusResult?>? PhoneAppBlockObserved;

    // Once per poller and again after each change, so a phone that was off
    // when the teacher set the block gets it the next time it's on. The phone
    // keeps an older policy out by SavedAt. Only a phone that took it counts
    // as delivered: an older one ("Ainda não funciona em celular.") or one
    // that couldn't save it is tried again after a while.
    private void SendPhoneAppBlockIfChanged()
    {
        if (_phoneAppPolicy?.Invoke() is not { } policy || policy.SavedAt == _sentAppBlockSavedAt
            || _machine.AgentThumbprint is not { Length: > 0 }
            || DateTime.UtcNow < _nextAppBlockAttempt || _stop.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var (succeeded, _) = PhoneAppBlockClient.Apply(_machine, _masterCertificate, policy, _agentPort);
            if (succeeded)
            {
                _sentAppBlockSavedAt = policy.SavedAt;
                _nextAppBlockObservation = DateTime.MinValue;
            }
            else
            {
                _nextAppBlockAttempt = DateTime.UtcNow + _agentRetryInterval;
            }
        }
        catch (Exception)
        {
            _nextAppBlockAttempt = DateTime.UtcNow + _agentRetryInterval;
        }
    }

    private void ObservePhoneAppBlock()
    {
        if (_machine.AgentThumbprint is not { Length: > 0 } || DateTime.UtcNow < _nextAppBlockObservation
            || _stop.IsCancellationRequested)
        {
            return;
        }

        PhoneAppBlockClient.StatusResult? status = null;
        try
        {
            status = PhoneAppBlockClient.GetStatus(_machine, _masterCertificate, _agentPort);
        }
        catch (Exception)
        {
            // The phone's agent didn't answer: no badge.
        }

        _nextAppBlockObservation = DateTime.UtcNow + AppBlockObserveInterval;
        if (!_stop.IsCancellationRequested)
        {
            PhoneAppBlockObserved?.Invoke(status);
        }
    }
}
