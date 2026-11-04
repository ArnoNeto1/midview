using Midview.Agent;
using Xunit;

public sealed class SwitchUserBlockHandlerTests
{
    private sealed class FakePolicy : ISwitchUserPolicy
    {
        public int Blocks { get; private set; }
        public int Unblocks { get; private set; }
        public bool Blocked { get; private set; }

        public void Block() { Blocks++; Blocked = true; }
        public void Unblock() { Unblocks++; Blocked = false; }
    }

    [Fact]
    public void Construction_ClearsAnyStaleBlock()
    {
        var policy = new FakePolicy();

        _ = new SwitchUserBlockHandler(policy);

        Assert.Equal(1, policy.Unblocks);
        Assert.Equal(0, policy.Blocks);
    }

    [Fact]
    public void TwoHolds_BlockOnce_AndRestoreAfterBoth()
    {
        var policy = new FakePolicy();
        var handler = new SwitchUserBlockHandler(policy);

        handler.Acquire();
        Assert.True(policy.Blocked);
        Assert.Equal(1, policy.Blocks);

        handler.Acquire(); // a second teacher's lock
        Assert.Equal(1, policy.Blocks);

        handler.Release();
        Assert.True(policy.Blocked); // the second hold still has it

        handler.Release();
        Assert.False(policy.Blocked);
    }
}
