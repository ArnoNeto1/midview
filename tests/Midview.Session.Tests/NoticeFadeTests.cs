using System.Drawing;
using Midview.Session;
using Xunit;

public class NoticeFadeTests
{
    // The strip as it sits at the top middle of a 1920-wide screen.
    private static readonly Rectangle Strip = new(700, 0, 520, 45);

    [Fact]
    public void TheMouse_OnTheStrip_IsNear()
    {
        Assert.True(NoticeFade.IsNear(Strip, new Point(960, 20), dpiScale: 1));
    }

    // Coming up to a button just under it already clears the way.
    [Fact]
    public void TheMouse_JustUnderTheStrip_IsNear()
    {
        Assert.True(NoticeFade.IsNear(Strip, new Point(960, 45 + 30), dpiScale: 1));
    }

    [Fact]
    public void TheMouse_ElsewhereOnTheScreen_IsNotNear()
    {
        Assert.False(NoticeFade.IsNear(Strip, new Point(960, 45 + 50), dpiScale: 1));
        Assert.False(NoticeFade.IsNear(Strip, new Point(100, 10), dpiScale: 1));
    }

    // On a screen at 200% everything is twice the pixels, the reach too.
    [Fact]
    public void HowNear_GrowsWithTheScreensScaling()
    {
        Assert.True(NoticeFade.IsNear(Strip, new Point(960, 45 + 70), dpiScale: 2));
        Assert.False(NoticeFade.IsNear(Strip, new Point(960, 45 + 90), dpiScale: 2));
    }

    [Fact]
    public void WithTheMouseNear_TheStripFades_InAFewTicks_AndStaysFaded()
    {
        var opacity = NoticeFade.Shown;
        for (var tick = 0; tick < 4; tick++)
        {
            opacity = NoticeFade.Next(opacity, near: true);
        }
        Assert.Equal(NoticeFade.Faded, opacity);
        Assert.Equal(NoticeFade.Faded, NoticeFade.Next(opacity, near: true));
    }

    [Fact]
    public void WhenTheMouseLeaves_TheStripComesBack_AndStaysAsItWas()
    {
        var opacity = NoticeFade.Faded;
        for (var tick = 0; tick < 4; tick++)
        {
            opacity = NoticeFade.Next(opacity, near: false);
        }
        Assert.Equal(NoticeFade.Shown, opacity);
        Assert.Equal(NoticeFade.Shown, NoticeFade.Next(opacity, near: false));
    }

    // Fully see-through would hide it from the student too, and fully solid
    // (1.0) would make WinForms drop the layered style that lets clicks
    // through it.
    [Fact]
    public void TheStrip_IsNeverGoneNorSolid()
    {
        Assert.InRange(NoticeFade.Faded, 0.05, 0.3);
        Assert.InRange(NoticeFade.Shown, 0.5, 0.95);
    }
}
