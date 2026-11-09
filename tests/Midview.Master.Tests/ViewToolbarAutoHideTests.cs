using Midview.Master;
using Xunit;

public class ViewToolbarAutoHideTests
{
    private static readonly DateTime Start = new(2026, 11, 9, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BeforeThePicture_TheToolbarStays()
    {
        var bar = new ViewToolbarAutoHide();

        bar.MouseMoved(Start, atTopEdge: false, onToolbar: false);

        Assert.True(bar.Shown);
        Assert.False(bar.Waiting);
        Assert.False(bar.Update(Start + TimeSpan.FromMinutes(1)));
        Assert.True(bar.Shown);
    }

    [Fact]
    public void ThePicture_HidesItAfterTheFirstDelay()
    {
        var bar = new ViewToolbarAutoHide();

        bar.PictureArrived(Start, mouseOnToolbar: false);

        Assert.True(bar.Waiting);
        Assert.False(bar.Update(Start + ViewToolbarAutoHide.FirstHideDelay - TimeSpan.FromMilliseconds(1)));
        Assert.True(bar.Shown);
        Assert.True(bar.Update(Start + ViewToolbarAutoHide.FirstHideDelay));
        Assert.False(bar.Shown);
        Assert.False(bar.Waiting);
    }

    [Fact]
    public void ThePicture_WithTheMouseOnTheToolbar_KeepsIt()
    {
        var bar = new ViewToolbarAutoHide();

        bar.PictureArrived(Start, mouseOnToolbar: true);

        Assert.False(bar.Waiting);
        Assert.True(bar.Shown);
    }

    [Fact]
    public void TheMouseOnTheToolbar_CancelsTheHiding()
    {
        var bar = new ViewToolbarAutoHide();
        bar.PictureArrived(Start, mouseOnToolbar: false);

        bar.MouseMoved(Start + TimeSpan.FromSeconds(1), atTopEdge: true, onToolbar: true);

        Assert.False(bar.Waiting);
        Assert.False(bar.Update(Start + TimeSpan.FromMinutes(1)));
        Assert.True(bar.Shown);
    }

    [Fact]
    public void TheMouseLeavingTheToolbar_HidesItAfterTheDelay()
    {
        var bar = new ViewToolbarAutoHide();
        bar.PictureArrived(Start, mouseOnToolbar: true);

        var off = Start + TimeSpan.FromSeconds(5);
        bar.MouseMoved(off, atTopEdge: false, onToolbar: false);
        // Moving on over the picture doesn't push the hiding back.
        bar.MouseMoved(off + TimeSpan.FromMilliseconds(500), atTopEdge: false, onToolbar: false);

        Assert.False(bar.Update(off + ViewToolbarAutoHide.HideDelay - TimeSpan.FromMilliseconds(1)));
        Assert.True(bar.Update(off + ViewToolbarAutoHide.HideDelay));
        Assert.False(bar.Shown);
    }

    [Fact]
    public void Hidden_RestingOnTheTopEdge_BringsItBack()
    {
        var bar = Hidden();

        var edge = Start + TimeSpan.FromSeconds(10);
        bar.MouseMoved(edge, atTopEdge: true, onToolbar: false);
        bar.MouseMoved(edge + TimeSpan.FromMilliseconds(100), atTopEdge: true, onToolbar: false);

        Assert.False(bar.Update(edge + ViewToolbarAutoHide.RevealDelay - TimeSpan.FromMilliseconds(1)));
        Assert.True(bar.Update(edge + ViewToolbarAutoHide.RevealDelay));
        Assert.True(bar.Shown);
    }

    [Fact]
    public void Hidden_PassingOverTheTopEdge_LeavesItHidden()
    {
        var bar = Hidden();

        var edge = Start + TimeSpan.FromSeconds(10);
        bar.MouseMoved(edge, atTopEdge: true, onToolbar: false);
        bar.MouseMoved(edge + TimeSpan.FromMilliseconds(100), atTopEdge: false, onToolbar: false);

        Assert.False(bar.Waiting);
        Assert.False(bar.Update(edge + TimeSpan.FromSeconds(5)));
        Assert.False(bar.Shown);
    }

    [Fact]
    public void Hidden_TheMouseLeavingTheWindow_ShowsItAtOnce()
    {
        var bar = Hidden();

        bar.MouseLeftWindow();

        Assert.True(bar.Shown);
        Assert.False(bar.Waiting);
    }

    [Fact]
    public void ALaterPicture_DoesNotHideItAgain()
    {
        var bar = new ViewToolbarAutoHide();
        bar.PictureArrived(Start, mouseOnToolbar: true);

        // The student's screen changing resolution while the teacher is on the buttons.
        bar.PictureArrived(Start + TimeSpan.FromSeconds(30), mouseOnToolbar: false);

        Assert.False(bar.Waiting);
        Assert.True(bar.Shown);
    }

    private static ViewToolbarAutoHide Hidden()
    {
        var bar = new ViewToolbarAutoHide();
        bar.PictureArrived(Start, mouseOnToolbar: false);
        Assert.True(bar.Update(Start + ViewToolbarAutoHide.FirstHideDelay));
        return bar;
    }
}
