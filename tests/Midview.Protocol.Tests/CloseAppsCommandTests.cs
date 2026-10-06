using Midview.Protocol;
using Xunit;

public class CloseAppsCommandTests
{
    [Theory]
    [InlineData(CloseAppsMode.Ask, 60)]
    [InlineData(CloseAppsMode.Force, 30)]
    [InlineData(CloseAppsMode.Force, 0)]
    [InlineData(CloseAppsMode.Ask, CloseAppsCommand.MaxDelaySeconds)]
    public void EncodeThenDecode_RoundTrips(CloseAppsMode mode, int delaySeconds)
    {
        Assert.True(CloseAppsCommand.TryDecode(CloseAppsCommand.Encode(mode, delaySeconds), out var decodedMode, out var decodedDelay));
        Assert.Equal(mode, decodedMode);
        Assert.Equal(delaySeconds, decodedDelay);
    }

    // What a MidviewMaster from before the warning sends: just the mode.
    [Fact]
    public void TheModeAlone_MeansNow()
    {
        Assert.True(CloseAppsCommand.TryDecode(new[] { (byte)CloseAppsMode.Force }, out var mode, out var delaySeconds));
        Assert.Equal(CloseAppsMode.Force, mode);
        Assert.Equal(0, delaySeconds);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 1 })]
    [InlineData(new byte[] { 0, 0, 60, 0 })]
    [InlineData(new byte[] { 7 })] // no such mode
    [InlineData(new byte[] { 7, 0, 60 })]
    [InlineData(new byte[] { 0, 0xFF, 0xFF })] // past the maximum delay
    public void TryDecode_RejectsAnythingElse(byte[] payload)
    {
        Assert.False(CloseAppsCommand.TryDecode(payload, out _, out _));
    }

    [Fact]
    public void Encode_RefusesADelayPastTheMaximum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CloseAppsCommand.Encode(CloseAppsMode.Ask, CloseAppsCommand.MaxDelaySeconds + 1));
    }

    [Theory]
    [InlineData(1, "1 segundo")]
    [InlineData(30, "30 segundos")]
    [InlineData(60, "1 minuto")]
    [InlineData(90, "1 minuto e 30 segundos")]
    [InlineData(300, "5 minutos")]
    public void DelayText_ReadsLikeTheStudentWouldSayIt(int delaySeconds, string expected)
    {
        Assert.Equal(expected, CloseAppsCommand.DelayText(delaySeconds));
    }
}
