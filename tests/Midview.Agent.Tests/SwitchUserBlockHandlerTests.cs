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

    private sealed class FailingPolicy : ISwitchUserPolicy
    {
        public void Block() => throw new UnauthorizedAccessException("sem acesso");
        public void Unblock() => throw new UnauthorizedAccessException("sem acesso");
    }

    // Switch user (HKLM) and the student's Task Manager and sign-out (their own
    // hive) ride on the same hold: one that fails leaves the other working.
    [Fact]
    public void APolicyThatFails_DoesNotStopTheOthers()
    {
        var log = new List<string>();
        var working = new FakePolicy();
        var handler = new SwitchUserBlockHandler(new ISwitchUserPolicy[] { new FailingPolicy(), working }, log.Add);

        handler.Acquire();
        Assert.True(working.Blocked);

        handler.Release();
        Assert.False(working.Blocked);
        Assert.Contains(log, line => line.Contains("sem acesso"));
    }
}
