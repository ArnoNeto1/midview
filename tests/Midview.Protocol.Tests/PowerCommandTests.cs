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
        Assert.True(PowerCommand.TryDecode(
            PowerCommand.Encode(action, delaySeconds), out var decodedAction, out var decodedDelay, out var installUpdates));
        Assert.Equal(action, decodedAction);
        Assert.Equal(delaySeconds, decodedDelay);
        Assert.False(installUpdates);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0, 0 })]
    [InlineData(new byte[] { 0, 0, 60, 0 })]
    [InlineData(new byte[] { 9, 0, 60 })] // no such action
    [InlineData(new byte[] { 0, 0xFF, 0xFF })] // past the maximum delay
    public void TryDecode_RejectsAnythingElse(byte[] payload)
    {
        Assert.False(PowerCommand.TryDecode(payload, out _, out _, out _));
    }

    [Fact]
    public void Encode_RefusesADelayPastTheMaximum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PowerCommand.Encode(PowerAction.Shutdown, PowerCommand.MaxDelaySeconds + 1));
    }

    // "Instalar as atualizações do Windows que estiverem esperando".
    [Theory]
    [InlineData(PowerAction.Shutdown, 60)]
    [InlineData(PowerAction.Restart, 0)]
    public void InstallingUpdates_RoundTrips(PowerAction action, int delaySeconds)
    {
        Assert.True(PowerCommand.TryDecode(
            PowerCommand.Encode(action, delaySeconds, installUpdates: true), out var decodedAction, out var decodedDelay, out var installUpdates));
        Assert.Equal(action, decodedAction);
        Assert.Equal(delaySeconds, decodedDelay);
        Assert.True(installUpdates);
    }

    // Without updates the request is the one every Agent already knows.
    [Fact]
    public void WithoutUpdates_TheRequestIsUnchanged()
    {
        Assert.Equal(new byte[] { 1, 0, 60 }, PowerCommand.Encode(PowerAction.Restart, 60, installUpdates: false));
        Assert.True(PowerCommand.TryDecode(new byte[] { 1, 0, 60 }, out _, out _, out var installUpdates));
        Assert.False(installUpdates);
    }

    // An Agent from before refuses it ("pedido inválido") instead of shutting
    // down without the updates the teacher asked for.
    [Fact]
    public void InstallingUpdates_IsLongerThanAnAgentFromBeforeAccepts() =>
        Assert.Equal(4, PowerCommand.Encode(PowerAction.Shutdown, 60, installUpdates: true).Length);

    [Theory]
    [InlineData(new byte[] { 0, 0, 60, 0 })] // a flags byte with nothing in it
    [InlineData(new byte[] { 0, 0, 60, 2 })] // a flag nobody knows
    [InlineData(new byte[] { 2, 0, 0, 1 })] // a cancel has nothing to install
    [InlineData(new byte[] { 0, 0, 60, 1, 0 })]
    public void TryDecode_RejectsFlagsItDoesNotKnow(byte[] payload) =>
        Assert.False(PowerCommand.TryDecode(payload, out _, out _, out _));

    [Fact]
    public void Encode_ACancelInstallsNothing() =>
        Assert.Throws<ArgumentException>(() => PowerCommand.Encode(PowerAction.Cancel, 0, installUpdates: true));
}
