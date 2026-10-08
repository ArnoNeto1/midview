using Midview.Master;
using Midview.Protocol;
using Xunit;

public class LabConfigChangeTests
{
    [Fact]
    public void With_ChangesOnlyTheOneFunction()
    {
        var before = new LabConfig(5, CleanOnExit: true, Lockdown: false, BlockDownloads: true,
            ForceGuest: false, Adblock: false, BlockEdge: false, LockTaskbar: false);

        var after = LabConfigChange.With(before, "lockdown", on: true);

        Assert.True(after.Lockdown);
        // everything else, SavedAt included, is left as it was
        Assert.Equal(before with { Lockdown = true }, after);
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("lockdown")]
    [InlineData("downloads")]
    [InlineData("guest")]
    [InlineData("adblock")]
    [InlineData("edge")]
    [InlineData("taskbar")]
    public void With_EveryButtonTag_TogglesSomething(string flag)
    {
        var off = LabConfig.Off(1);

        var on = LabConfigChange.With(off, flag, on: true);
        Assert.True(on.IsAnyOn);
        Assert.False(LabConfigChange.With(on, flag, on: false).IsAnyOn);
    }

    [Fact]
    public void With_AnUnknownTag_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LabConfigChange.With(LabConfig.Off(1), "usb", true));
    }

    [Fact]
    public void NextSavedAt_IsAlwaysNewerThanTheMachines()
    {
        Assert.Equal(1000, LabConfigChange.NextSavedAt(1000, 200));   // now is newer
        Assert.Equal(201, LabConfigChange.NextSavedAt(100, 200));     // machine is ahead (clock skew): still beat it
        Assert.Equal(201, LabConfigChange.NextSavedAt(200, 200));     // same second: still strictly newer
    }
}
