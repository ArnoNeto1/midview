using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

public class TitleLogoTests
{
    // The sizes in assets\midview.ico, in the order the file holds them.
    private static readonly int[] IconSizes = { 128, 96, 64, 48, 256, 40, 32, 24, 20, 16 };

    [Theory]
    [InlineData(20.0, 20)]  // 100%: the 20 px picture as it is
    [InlineData(25.0, 32)]  // 125%: scaled down from 32, never up from 24
    [InlineData(16.0, 16)]
    [InlineData(40.0, 40)]  // 200%
    [InlineData(300.0, 256)] // bigger than any: the biggest
    public void LogoFrameSize_IsTheSmallestPictureAtLeastAsBigAsTheLogoOnScreen(double neededPixels, int expected) =>
        Assert.Equal(expected, MainWindow.LogoFrameSize(IconSizes, neededPixels));
}
