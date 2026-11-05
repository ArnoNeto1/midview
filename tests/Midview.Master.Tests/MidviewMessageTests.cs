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

    // Windows' order ("Sim | Não") for every message box; "Sim" in the danger
    // style only for a warning (the answers with no way back), whatever the default.
    [Theory]
    [InlineData(MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.None, QuestionButtons.YesFirst)]
    [InlineData(MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.None, QuestionButtons.YesFirst)]
    [InlineData(MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.None, QuestionButtons.YesFirst)]
    [InlineData(MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.None, QuestionButtons.YesFirst)]
    [InlineData(MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes, QuestionButtons.YesFirst)]
    [InlineData(MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, QuestionButtons.YesFirst)]
    [InlineData(MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.None, QuestionButtons.YesFirstDanger)]
    [InlineData(MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, QuestionButtons.YesFirstDanger)]
    public void ButtonsFor_KeepsWindowsOrderAndMarksTheDangerousYes(
        MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult, QuestionButtons expected)
    {
        Assert.Equal(expected, MidviewMessage.ButtonsFor(buttons, image, defaultResult));
    }

    // The questions with their own words keep the action on the right, in the accent color.
    [Fact]
    public void QuestionContent_DefaultsToTheActionOnTheRight()
    {
        Assert.Equal(QuestionButtons.ActionOnRight, new QuestionContent("t", "", "h", "Atualizar os alunos", "Agora não").Buttons);
    }

    [Theory]
    [InlineData(QuestionButtons.ActionOnRight, false, "PrimaryButton")]
    [InlineData(QuestionButtons.YesFirst, true, "PrimaryButton")]
    [InlineData(QuestionButtons.YesFirstDanger, true, "DangerButton")]
    public void Arrangement_PlacesAndStylesTheYesButton(QuestionButtons buttons, bool yesFirst, string yesStyle)
    {
        Assert.Equal((yesFirst, yesStyle), QuestionWindow.Arrangement(buttons));
    }
}
