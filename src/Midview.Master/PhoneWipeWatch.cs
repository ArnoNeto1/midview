namespace Midview.Master;

// What the watch of a phone just told to wipe itself says.
internal enum WipeWatchResult
{
    // Still waiting: the phone answered, but the time is not up yet.
    Waiting,

    // The phone stopped answering: it is restarting to be erased.
    Wiping,

    // The phone still answers long after it acked the wipe: the wipe failed
    // on the phone (it acks first, then wipes).
    NotWiped,
}

// After a school phone acked "Apagar e preparar de novo": a phone that is
// really wiping restarts within seconds and stops answering; one that still
// answers 90 seconds later did not wipe. Once it stopped answering, it is
// wiping, whatever comes after.
internal sealed class PhoneWipeWatch
{
    public static readonly TimeSpan NotWipedAfter = TimeSpan.FromSeconds(90);

    private readonly DateTimeOffset _since;
    private bool _wentAway;

    public PhoneWipeWatch(DateTimeOffset ackedAt)
    {
        _since = ackedAt;
    }

    public WipeWatchResult Tick(DateTimeOffset now, bool online)
    {
        if (_wentAway || !online)
        {
            _wentAway = true;
            return WipeWatchResult.Wiping;
        }
        return now - _since >= NotWipedAfter ? WipeWatchResult.NotWiped : WipeWatchResult.Waiting;
    }
}
