using Midview.Master;
using Xunit;

public class MachineCountTextTests
{
    // The classroom window's title bar chip: how many of the room's machines
    // answer. Nothing at all while the room is empty.
    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(1, 18, "1 de 18 ligada")]
    [InlineData(16, 18, "16 de 18 ligadas")]
    [InlineData(0, 31, "0 de 31 ligadas")]
    [InlineData(31, 31, "31 de 31 ligadas")]
    public void Online_CountsTheMachinesThatAnswer(int online, int total, string expected)
    {
        Assert.Equal(expected, MachineCountText.Online(online, total));
    }
}
