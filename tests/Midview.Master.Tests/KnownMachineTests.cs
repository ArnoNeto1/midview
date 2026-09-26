using Midview.Master;
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
    public void IsSameEntryAs_IgnoresOnlyTheMacAddress()
    {
        var saved = Learned with { MacAddress = null };

        Assert.True(Learned.IsSameEntryAs(saved));
        Assert.False(Learned.IsSameEntryAs(saved with { HostAddress = "192.168.0.99" }));
        Assert.False(Learned.IsSameEntryAs(null));
    }
}
