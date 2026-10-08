using Midview.Protocol;
using Xunit;

public class LabConfigTests
{
    [Fact]
    public void EncodeThenDecode_AllOn_RoundTrips()
    {
        var config = new LabConfig(1739000000, true, true, true, true, true, true, true);

        Assert.True(LabConfig.TryDecode(config.Encode(), out var decoded));
        Assert.Equal(config, decoded);
        Assert.True(decoded.IsAnyOn);
    }

    [Fact]
    public void EncodeThenDecode_Off_RoundTrips()
    {
        var off = LabConfig.Off(42);

        Assert.True(LabConfig.TryDecode(off.Encode(), out var decoded));
        Assert.Equal(off, decoded);
        Assert.False(decoded.IsAnyOn);
    }

    [Fact]
    public void EncodeThenDecode_OneFlag_KeepsOnlyThatFlag()
    {
        var config = LabConfig.Off(7) with { BlockEdge = true };

        Assert.True(LabConfig.TryDecode(config.Encode(), out var decoded));
        Assert.True(decoded.BlockEdge);
        Assert.False(decoded.Lockdown);
        Assert.Equal(7, decoded.SavedAt);
    }

    [Theory]
    [InlineData(0)]   // empty
    [InlineData(9)]   // one byte short
    [InlineData(11)]  // one byte long
    public void Decode_WrongLength_IsFalse(int length)
    {
        Assert.False(LabConfig.TryDecode(new byte[length], out _));
    }

    [Fact]
    public void Decode_WrongFormatVersion_IsFalse()
    {
        var payload = LabConfig.Off(1).Encode();
        payload[0] = 2;

        Assert.False(LabConfig.TryDecode(payload, out _));
    }

    [Fact]
    public void Decode_AnUnknownFlagBit_IsFalse()
    {
        // A newer Master against this version must not apply a flag this
        // version doesn't understand.
        var payload = (LabConfig.Off(1) with { Lockdown = true }).Encode();
        payload[9] |= 0b1000_0000; // a bit beyond LockTaskbar (64)

        Assert.False(LabConfig.TryDecode(payload, out _));
    }

    [Fact]
    public void SameLockdownsAs_IgnoresSavedAt()
    {
        var a = new LabConfig(100, true, false, true, false, false, false, false);
        var b = new LabConfig(999, true, false, true, false, false, false, false);
        var c = b with { ForceGuest = true };

        Assert.True(a.SameLockdownsAs(b));
        Assert.False(a.SameLockdownsAs(c));
    }
}
