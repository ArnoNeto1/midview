using Midview.Master;
using Xunit;

// How much a drive's Recycle Bin holds, from its settings (never read from the
// real ones here).
public class RecycleBinRoomTests
{
    private const long MB = 1024 * 1024;
    private const long GB = 1024 * MB;

    [Fact]
    public void Capacity_IsWhatTheRecycleBinIsSetTo() =>
        Assert.Equal(2048 * MB, RecycleBinRoom.Capacity(configuredMegabytes: 2048, deletesForGood: false, driveBytes: 500 * GB));

    [Fact]
    public void Capacity_OfARecycleBinThatDeletesForGood_IsNothing() =>
        Assert.Equal(0, RecycleBinRoom.Capacity(configuredMegabytes: 2048, deletesForGood: true, driveBytes: 500 * GB));

    // Settings that can't be read: a tenth of the drive.
    [Fact]
    public void Capacity_NotKnown_IsATenthOfTheDrive() =>
        Assert.Equal(50 * GB, RecycleBinRoom.Capacity(configuredMegabytes: null, deletesForGood: false, driveBytes: 500 * GB));
}
