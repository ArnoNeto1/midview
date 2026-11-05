using System.Windows.Media;
using Midview.Master;
using Xunit;

public class DarkTitleBarTests
{
    // Windows takes the caption color as a COLORREF: 0x00BBGGRR.
    [Fact]
    public void ColorRef_IsBlueGreenRed()
    {
        Assert.Equal(0x001E110Au, DarkTitleBar.ColorRef(Color.FromRgb(0x0A, 0x11, 0x1E)));
    }
}
