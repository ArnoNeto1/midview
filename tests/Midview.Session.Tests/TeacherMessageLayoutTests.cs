using System.Drawing;
using Midview.Session;
using Xunit;

public class TeacherMessageLayoutTests
{
    private static readonly Size Box = new(700, 230);

    // A stand-in for measuring a message: its height grows with the font
    // size, by pixelsPerPoint for each point.
    private static Func<float, Size> TextOfHeightPerPoint(int pixelsPerPoint) =>
        size => new Size(600, (int)(size * pixelsPerPoint));

    [Fact]
    public void AShortMessage_GetsTheBiggestSize()
    {
        Assert.Equal(TeacherMessageLayout.MaxFontSize,
            TeacherMessageLayout.FitFontSize(Box, TextOfHeightPerPoint(5)));
    }

    [Fact]
    public void ALongerMessage_ShrinksUntilItFits()
    {
        // 15 px per point: 230 px fits up to 15 pt.
        Assert.Equal(15f, TeacherMessageLayout.FitFontSize(Box, TextOfHeightPerPoint(15)));
    }

    [Fact]
    public void AMessageTooLongEvenAtTheSmallestSize_Scrolls()
    {
        Assert.Null(TeacherMessageLayout.FitFontSize(Box, TextOfHeightPerPoint(40)));
    }

    [Fact]
    public void ItsTexts_AreTheTeachersVoice()
    {
        Assert.Equal("Mensagem do professor", TeacherMessageLayout.Header);
        Assert.Equal("OK, ENTENDI", TeacherMessageLayout.OkButton);
    }
}
