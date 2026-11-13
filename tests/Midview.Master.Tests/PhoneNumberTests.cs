using Midview.Master;
using Xunit;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("7", "07")]
    [InlineData(" 12 ", "12")]
    [InlineData("105", "105")]
    [InlineData("0", "00")]
    [InlineData("007", "07")]
    [InlineData("000", "00")]
    [InlineData("010", "10")]
    public void Normalize_pads_to_two_digits(string typed, string expected) =>
        Assert.Equal(expected, PhoneNumber.Normalize(typed));

    [Theory]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("7a")]
    [InlineData("-1")]
    public void Normalize_refuses_bad_input(string typed) =>
        Assert.Null(PhoneNumber.Normalize(typed));

    [Fact]
    public void NameOf_prefixes_celular() =>
        Assert.Equal("Celular 07", PhoneNumber.NameOf("07"));

    [Theory]
    [InlineData(AndroidDeviceKind.Phone, "Celular 07")]
    [InlineData(AndroidDeviceKind.Tablet, "Tablet 07")]
    public void NameOf_follows_the_kind(AndroidDeviceKind kind, string expected) =>
        Assert.Equal(expected, PhoneNumber.NameOf("07", kind));

    // The name is the only tablet marker; any other name is a phone's.
    [Theory]
    [InlineData("Tablet 07", AndroidDeviceKind.Tablet)]
    [InlineData("Tablet 105", AndroidDeviceKind.Tablet)]
    [InlineData("Celular 07", AndroidDeviceKind.Phone)]
    [InlineData("Cel 01", AndroidDeviceKind.Phone)]
    [InlineData("tablet 07", AndroidDeviceKind.Phone)]
    [InlineData("Tablet07", AndroidDeviceKind.Phone)]
    [InlineData("Meu Tablet 07", AndroidDeviceKind.Phone)]
    [InlineData("", AndroidDeviceKind.Phone)]
    public void KindOf_reads_the_name(string name, AndroidDeviceKind expected) =>
        Assert.Equal(expected, PhoneNumber.KindOf(name));

    [Theory]
    [InlineData(AndroidDeviceKind.Phone)]
    [InlineData(AndroidDeviceKind.Tablet)]
    public void KindOf_reads_back_NameOf(AndroidDeviceKind kind) =>
        Assert.Equal(kind, PhoneNumber.KindOf(PhoneNumber.NameOf("07", kind)));

    // The assistant's ready page counts only the kind it prepares; PCs never count.
    [Fact]
    public void CountOf_counts_one_kind_of_android_device()
    {
        var android = Midview.Protocol.DeviceKind.Android;
        var machines = new[]
        {
            new KnownMachine("Celular 01", "192.0.2.1", "ANY", null, Kind: android),
            new KnownMachine("Celular 549C", "192.0.2.2", "ANY", null, Kind: android),
            new KnownMachine("Tablet 01", "192.0.2.3", "ANY", null, Kind: android),
            new KnownMachine("Tablet 02", "192.0.2.4", "ANY", null, Kind: android),
            new KnownMachine("Tablet 03", "192.0.2.5", "ANY", null, Kind: android),
            new KnownMachine("LAB-01", "192.0.2.6", "ANY", null),
            new KnownMachine("Tablet 09", "192.0.2.7", "ANY", null),
        };

        Assert.Equal(2, PhoneNumber.CountOf(machines, AndroidDeviceKind.Phone));
        Assert.Equal(3, PhoneNumber.CountOf(machines, AndroidDeviceKind.Tablet));
        Assert.Equal(0, PhoneNumber.CountOf(Array.Empty<KnownMachine>(), AndroidDeviceKind.Tablet));
    }
}
