using System.Windows;
using Midview.Master;
using Xunit;

public class ViewWindowFitTests
{
    [Fact]
    public void ASmallScreen_OpensAtItsOwnSize()
    {
        var size = ViewWindowFit.ClientSize(1024, 768, 1, 1, new Size(1900, 1000));

        Assert.Equal(new Size(1024, 768), size);
    }

    [Fact]
    public void OnAScaledTeacherScreen_OneStudentPixelIsOneScreenPixel()
    {
        var size = ViewWindowFit.ClientSize(1000, 500, 1.25, 1.25, new Size(1500, 800));

        Assert.Equal(800, size.Width, 6);
        Assert.Equal(400, size.Height, 6);
    }

    [Fact]
    public void ABigScreen_ShrinksToFit_KeepingItsShape()
    {
        // A Full HD student in the room left by the title bar on a Full HD teacher.
        var size = ViewWindowFit.ClientSize(1920, 1080, 1, 1, new Size(1904, 1001));

        Assert.Equal(1001, size.Height, 6);
        Assert.Equal(1001 * 16.0 / 9, size.Width, 6);
    }

    [Fact]
    public void ANarrowRoom_LimitsTheWidth()
    {
        var size = ViewWindowFit.ClientSize(1920, 1080, 1, 1, new Size(960, 1000));

        Assert.Equal(960, size.Width, 6);
        Assert.Equal(540, size.Height, 6);
    }

    [Fact]
    public void Portrait_screen_fits_the_view_window()
    {
        // A 720x1650 phone in the room of a Full HD teacher: standing, no bands.
        var size = ViewWindowFit.ClientSize(720, 1650, 1, 1, new Size(1904, 1001));

        Assert.Equal(1001, size.Height, 6);
        Assert.Equal(1001 * 720.0 / 1650, size.Width, 6);
        Assert.True(size.Height > size.Width);
    }

    [Fact]
    public void Portrait_screen_on_a_scaled_screen_keeps_its_shape()
    {
        var size = ViewWindowFit.ClientSize(559, 1280, 1.5, 1.5, new Size(1269, 667));

        Assert.Equal(559.0 / 1280, size.Width / size.Height, 6);
        Assert.True(size.Height <= 667 + 1e-9);
    }

    [Theory]
    [InlineData(0, 1080, 1, 1, 1000, 1000)]
    [InlineData(1920, 0, 1, 1, 1000, 1000)]
    [InlineData(1920, 1080, 0, 1, 1000, 1000)]
    [InlineData(1920, 1080, 1, 1, 0, 1000)]
    [InlineData(1920, 1080, 1, 1, 1000, -5)]
    public void NoPictureOrNoRoom_IsEmpty(int width, int height, double dpiX, double dpiY, double roomWidth, double roomHeight)
    {
        var size = ViewWindowFit.ClientSize(width, height, dpiX, dpiY, new Size(roomWidth, Math.Max(0, roomHeight)));

        Assert.True(size.IsEmpty);
    }
}
