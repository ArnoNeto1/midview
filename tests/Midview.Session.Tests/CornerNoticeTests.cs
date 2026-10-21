using System.Drawing;
using Midview.Session;
using Xunit;

// The lesson-start notice's window (WinFormsCornerNotice): where it goes and
// how long it stays. The form itself is not shown here.
public class CornerNoticeTests
{
    [Fact]
    public void TheNotice_SitsInTheBottomRightCorner_OfTheWorkingArea()
    {
        var area = new Rectangle(0, 0, 1920, 1040); // the taskbar below it

        var at = WinFormsCornerNotice.PlaceIn(area, new Size(400, 120));

        Assert.Equal(new Point(1920 - 400 - WinFormsCornerNotice.Margin, 1040 - 120 - WinFormsCornerNotice.Margin), at);
    }

    [Fact]
    public void TheNotice_StaysInAWorkingAreaThatDoesNotStartAtTheOrigin()
    {
        var area = new Rectangle(60, 40, 1860, 1040); // the taskbar on the left, a bar above

        var at = WinFormsCornerNotice.PlaceIn(area, new Size(400, 120));

        Assert.Equal(new Point(60 + 1860 - 400 - WinFormsCornerNotice.Margin, 40 + 1040 - 120 - WinFormsCornerNotice.Margin), at);
    }

    [Fact]
    public void TheNotice_GoesAwayOnItsOwn_AfterAFewSeconds()
    {
        Assert.InRange(WinFormsCornerNotice.Lifetime, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }
}
