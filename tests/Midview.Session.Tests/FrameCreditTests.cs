using System.Diagnostics;
using Midview.Session;
using Xunit;

public class FrameCreditTests
{
    [Fact]
    public void Send_NumbersUpdatesFromOne()
    {
        var credit = new FrameCredit();

        Assert.Equal(1, credit.Send(0));
        Assert.Equal(2, credit.Send(0));
    }

    [Fact]
    public void WaitForRoom_WithRoom_ReturnsAtOnce()
    {
        var credit = new FrameCredit();
        credit.Send(0);

        var sw = Stopwatch.StartNew();
        Assert.True(credit.WaitForRoom(TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    // With a timeout of a minute, only the acknowledgement itself can wake
    // the waiting thread this soon.
    [Fact]
    public async Task WaitForRoom_Full_IsWokenByAnAcknowledgement()
    {
        var credit = new FrameCredit();
        credit.Send(0);
        credit.Send(0);

        var waiting = Task.Run(() => credit.WaitForRoom(TimeSpan.FromMinutes(1), CancellationToken.None));
        await Task.Delay(300);
        Assert.False(waiting.IsCompleted);

        credit.Acknowledge(1, 0);

        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void WaitForRoom_NoAcknowledgement_GivesUpOnTheOldestAfterTheTimeout()
    {
        var credit = new FrameCredit();
        credit.Send(0);
        credit.Send(0);

        var sw = Stopwatch.StartNew();
        Assert.True(credit.WaitForRoom(TimeSpan.FromMilliseconds(300), CancellationToken.None));

        Assert.InRange(sw.ElapsedMilliseconds, 250, 5000);
        credit.Send(0); // room for one again
        sw.Restart();
        Assert.True(credit.WaitForRoom(TimeSpan.FromMilliseconds(300), CancellationToken.None));
        Assert.InRange(sw.ElapsedMilliseconds, 250, 5000); // and full again after it
    }

    [Fact]
    public void WaitForRoom_AlreadyCancelled_ReturnsFalseAtOnce()
    {
        var credit = new FrameCredit();
        credit.Send(0);
        credit.Send(0);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var sw = Stopwatch.StartNew();
        Assert.False(credit.WaitForRoom(TimeSpan.FromSeconds(10), cancelled.Token));

        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public void WaitForRoom_Cancelled_EvenWithRoom_ReturnsFalse()
    {
        var credit = new FrameCredit();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.False(credit.WaitForRoom(TimeSpan.FromSeconds(10), cancelled.Token));
    }

    // The connection closing while the capture thread waits: it must stop
    // waiting at once, not after the timeout.
    [Fact]
    public async Task WaitForRoom_CancelledWhileWaiting_ReturnsFalsePromptly()
    {
        var credit = new FrameCredit();
        credit.Send(0);
        credit.Send(0);
        using var closing = new CancellationTokenSource();

        var waiting = Task.Run(() => credit.WaitForRoom(TimeSpan.FromMinutes(1), closing.Token));
        await Task.Delay(300);
        Assert.False(waiting.IsCompleted);

        closing.Cancel();

        Assert.False(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Acknowledge_ALaterUpdate_AcknowledgesEveryOneBefore()
    {
        var credit = new FrameCredit();
        credit.Send(100);
        credit.Send(150);

        Assert.Equal(60, credit.Acknowledge(2, 210)); // how long update 2 took

        var sw = Stopwatch.StartNew();
        credit.Send(0);
        Assert.True(credit.WaitForRoom(TimeSpan.FromSeconds(10), CancellationToken.None)); // only update 3 is waiting
        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    // An update too big for one message goes as several: it counts as one
    // update, done when its last message is acknowledged.
    [Fact]
    public void ASplitUpdate_IsOneUpdate_AcknowledgedByItsLastMessage()
    {
        var credit = new FrameCredit();

        Assert.Equal(1, credit.Send(100, messages: 3)); // messages 1, 2 and 3
        Assert.Equal(4, credit.Send(150));
        Assert.Null(credit.Acknowledge(2, 200)); // only part of the first update

        var sw = Stopwatch.StartNew();
        Assert.True(credit.WaitForRoom(TimeSpan.FromMilliseconds(300), CancellationToken.None));
        Assert.True(sw.ElapsedMilliseconds >= 250); // still two updates waiting: it had to give one up

        Assert.Equal(60, credit.Acknowledge(4, 210));
    }

    [Fact]
    public void Acknowledge_NothingWaiting_IsNull()
    {
        var credit = new FrameCredit();
        credit.Send(0);
        credit.Acknowledge(1, 10);

        Assert.Null(credit.Acknowledge(1, 20));
    }
}
