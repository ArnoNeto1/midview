using Midview.Protocol;
using Xunit;

public class PowerCommandTests
{
    [Theory]
    [InlineData(PowerAction.Shutdown, 60)]
    [InlineData(PowerAction.Restart, 0)]
    [InlineData(PowerAction.Cancel, 0)]
    [InlineData(PowerAction.Shutdown, PowerCommand.MaxDelaySeconds)]
    public void EncodeThenDecode_RoundTrips(PowerAction action, int delaySeconds)
    {
        Assert.True(PowerCommand.TryDecode(PowerCommand.Encode(action, delaySeconds), out var decodedAction, out var decodedDelay));
        Assert.Equal(action, decodedAction);
        Assert.Equal(delaySeconds, decodedDelay);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0, 0 })]
    [InlineData(new byte[] { 0, 0, 60, 0 })]
    [InlineData(new byte[] { 9, 0, 60 })] // no such action
    [InlineData(new byte[] { 0, 0xFF, 0xFF })] // past the maximum delay
    public void TryDecode_RejectsAnythingElse(byte[] payload)
    {
        Assert.False(PowerCommand.TryDecode(payload, out _, out _));
    }

    [Fact]
    public void Encode_RefusesADelayPastTheMaximum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PowerCommand.Encode(PowerAction.Shutdown, PowerCommand.MaxDelaySeconds + 1));
    }
}
