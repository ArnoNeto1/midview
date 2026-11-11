using Midview.Master;
using Midview.Protocol;
using Xunit;

public class KnownMachineTests
{
    private static readonly KnownMachine Learned = new("Aluno-1", "192.168.0.21", "SESSION", "AGENT", "00-1A-2B-3C-4D-5E");

    [Fact]
    public void KeepingMacAddressOf_TheSameHostWithoutOne_KeepsTheOneKnown() =>
        Assert.Equal("00-1A-2B-3C-4D-5E",
            new KnownMachine("Aluno-1", "192.168.0.21", "NEWSESSION", "AGENT").KeepingMacAddressOf(Learned).MacAddress);

    [Fact]
    public void KeepingMacAddressOf_ANewerOne_Wins() =>
        Assert.Equal("00-11-22-33-44-55",
            (Learned with { MacAddress = "00-11-22-33-44-55" }).KeepingMacAddressOf(Learned).MacAddress);

    [Fact]
    public void KeepingMacAddressOf_AnotherHost_DoesNotKeepIt() =>
        Assert.Null(new KnownMachine("Aluno-1", "192.168.0.99", "SESSION", "AGENT").KeepingMacAddressOf(Learned).MacAddress);

    [Fact]
    public void KeepingKindOf_AnEditedPhone_StaysAPhone()
    {
        var phone = new KnownMachine("Cel 01", "192.168.0.30", "SESSION", null, Kind: DeviceKind.Android);
        var edited = new KnownMachine("Cel 01", "192.168.0.30", "SESSION", null).KeepingKindOf(phone);

        Assert.True(edited.IsPhone);
    }

    [Fact]
    public void KeepingKindOf_ANewPcThatOnlySharesThePhonesName_StaysAComputer()
    {
        var phone = new KnownMachine("Cel 01", "192.168.0.30", "SESSION", null, Kind: DeviceKind.Android);

        Assert.False(new KnownMachine("Cel 01", "192.168.0.77", "SESSION2", "AGENT").KeepingKindOf(phone).IsPhone);
    }

    [Fact]
    public void KeepingKindOf_AComputer_StaysAComputer() =>
        Assert.False(new KnownMachine("Aluno-1", "192.168.0.21", "SESSION", "AGENT").KeepingKindOf(Learned).IsPhone);

    [Fact]
    public void IsSameEntryAs_IgnoresOnlyTheMacAddress()
    {
        var saved = Learned with { MacAddress = null };

        Assert.True(Learned.IsSameEntryAs(saved));
        Assert.False(Learned.IsSameEntryAs(saved with { HostAddress = "192.168.0.99" }));
        Assert.False(Learned.IsSameEntryAs(null));
    }
}
