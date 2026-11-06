using Midview.Agent;
using Xunit;

// The Agent's side of the lock signal the Desk helper reads. A Local\ name per
// test, so nothing here needs the Global namespace the service uses.
public sealed class ScreenLockSignalTests
{
    private readonly string _name = $@"Local\MidviewTest-{Guid.NewGuid():N}";

    private bool IsSet()
    {
        using var opened = EventWaitHandle.OpenExisting(_name);
        return opened.WaitOne(0);
    }

    [Fact]
    public void Block_SetsIt_AndUnblock_ClearsIt()
    {
        using var signal = new ScreenLockSignal(_name);
        Assert.False(IsSet());

        signal.Block();
        Assert.True(IsSet());

        signal.Unblock();
        Assert.False(IsSet());
    }

    // An Agent starting again never inherits a lock that was on before.
    [Fact]
    public void ANewSignal_StartsUnlocked()
    {
        using var earlier = new ScreenLockSignal(_name);
        earlier.Block();

        using var next = new ScreenLockSignal(_name);

        Assert.False(IsSet());
    }

    // The signal goes with the Agent: once it is gone the helper reads "unlocked".
    [Fact]
    public void Disposed_TheEventIsGone()
    {
        var signal = new ScreenLockSignal(_name);
        signal.Block();

        signal.Dispose();

        Assert.False(EventWaitHandle.TryOpenExisting(_name, out _));
    }
}
