using System.Windows;
using Xunit;

namespace Midview.Master.Tests;

public class MidviewMessageTests
{
    [Fact]
    public void Labels_OkIsOneButton()
    {
        Assert.Equal(("OK", (string?)null), MidviewMessage.Labels(MessageBoxButton.OK));
    }

    [Fact]
    public void Labels_YesNoIsSimNao()
    {
        Assert.Equal(("Sim", (string?)"Não"), MidviewMessage.Labels(MessageBoxButton.YesNo));
    }

    [Theory]
    [InlineData(MessageBoxImage.Information, "")]
    [InlineData(MessageBoxImage.Warning, "")]
    [InlineData(MessageBoxImage.Error, "")]
    [InlineData(MessageBoxImage.Question, "")]
    public void IconFor_EachImageHasAGlyph(MessageBoxImage image, string glyph)
    {
        Assert.Equal(glyph, MidviewMessage.IconFor(image));
    }
}
