using Midview.Session;
using Xunit;

// The lock screen's state, seen from another process: each check opens the
// name afresh, as the countdown banner (a second Session process) does.
public class ScreenLockStateTests
{
    private static string NewName() => $@"Local\Midview.Teste.{Guid.NewGuid():N}";

    [Fact]
    public void NobodyPublished_MeansNotLocked()
    {
        Assert.False(new ScreenLockState(NewName()).IsLocked);
    }

    [Fact]
    public void WhatTheOwnerPublishes_IsWhatOthersSee()
    {
        var name = NewName();
        var owner = new ScreenLockState(name);
        var other = new ScreenLockState(name);

        owner.Publish(true);
        Assert.True(owner.IsLocked);
        Assert.True(other.IsLocked);

        owner.Publish(false);
        Assert.False(owner.IsLocked);
        Assert.False(other.IsLocked);
    }
}
