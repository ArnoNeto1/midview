using Midview.Desk;
using Xunit;

public class DesktopFollowerTests
{
    [Fact]
    public void Reports_change_only_when_the_input_desktop_actually_changes()
    {
        var a = new IntPtr(0xA);
        var b = new IntPtr(0xB);
        var script = new Queue<IntPtr>(new[] { a, a, b, b, a });
        var follower = new DesktopFollower(() => script.Dequeue());

        // 1st poll: nothing reported yet, so the first value is a change.
        Assert.True(follower.PollChanged(out var desktop));
        Assert.Equal(a, desktop);

        // 2nd poll: still A.
        Assert.False(follower.PollChanged(out _));

        // 3rd poll: moved to B.
        Assert.True(follower.PollChanged(out desktop));
        Assert.Equal(b, desktop);

        // 4th poll: still B.
        Assert.False(follower.PollChanged(out _));

        // 5th poll: back to A, which is a change from B even though A was seen before.
        Assert.True(follower.PollChanged(out desktop));
        Assert.Equal(a, desktop);
    }

    [Fact]
    public void Reports_a_change_again_after_Reset()
    {
        var a = new IntPtr(0xA);
        var follower = new DesktopFollower(() => a);

        Assert.True(follower.PollChanged(out _));   // first poll: a change
        Assert.False(follower.PollChanged(out _));  // same desktop: no change

        follower.Reset();

        // After a reset the next poll reports a change again, though the desktop
        // is unchanged -- a fresh control session must re-attach and capture.
        Assert.True(follower.PollChanged(out var desktop));
        Assert.Equal(a, desktop);
    }
}
