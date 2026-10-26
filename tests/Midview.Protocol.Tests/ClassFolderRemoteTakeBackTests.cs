using Midview.Protocol;
using Xunit;

// MessageType.RemoteTakeBack: a PC whose take-over can't be told
// done asks the home to claim the room back above the generation it gave up.
public sealed class ClassFolderRemoteTakeBackTests
{
    [Fact]
    public void RemoteTakeBack_FollowsRemoteHandOverDone()
    {
        Assert.Equal(85, (int)MessageType.RemoteTakeBack);
        Assert.Equal((int)MessageType.RemoteHandOverDone + 1, (int)MessageType.RemoteTakeBack);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(639_281_106_000_000_001L)]
    [InlineData(long.MaxValue)]
    public void TakeBack_RoundTrips(long abandoned)
    {
        var payload = ClassFolderRemote.EncodeTakeBack(abandoned);

        Assert.Equal(8, payload.Length);
        Assert.True(ClassFolderRemote.TryDecodeTakeBack(payload, out var decoded));
        Assert.Equal(abandoned, decoded);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x80, 0, 0, 0, 0, 0, 0, 1 })]
    public void TakeBack_RefusesAnythingButAGeneration(byte[] payload)
    {
        Assert.False(ClassFolderRemote.TryDecodeTakeBack(payload, out _));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void TakeBack_OnlyEncodesAGeneration(long abandoned)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClassFolderRemote.EncodeTakeBack(abandoned));
    }
}
