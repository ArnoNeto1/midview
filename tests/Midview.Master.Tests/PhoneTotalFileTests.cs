using Midview.Master;
using Xunit;

public class PhoneTotalFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-total-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Nothing_saved_is_not_asked_yet()
    {
        Assert.False(PhoneTotalFile.Asked(_folder));
        Assert.Null(PhoneTotalFile.Load(_folder));
    }

    [Fact]
    public void A_total_round_trips()
    {
        PhoneTotalFile.Save(11, _folder);

        Assert.True(PhoneTotalFile.Asked(_folder));
        Assert.Equal(11, PhoneTotalFile.Load(_folder));
        Assert.Equal("11", File.ReadAllText(Path.Combine(_folder, "phone-total.txt")).Trim());
    }

    // Left blank: asked once all the same, never again.
    [Fact]
    public void No_total_is_remembered_as_asked()
    {
        PhoneTotalFile.Save(null, _folder);

        Assert.True(PhoneTotalFile.Asked(_folder));
        Assert.Null(PhoneTotalFile.Load(_folder));
    }

    [Theory]
    [InlineData("11", 11)]
    [InlineData(" 3 ", 3)]
    [InlineData("", null)]
    [InlineData("0", null)]
    [InlineData("onze", null)]
    [InlineData("1000", null)]
    public void Parse_takes_1_to_999(string typed, int? expected) =>
        Assert.Equal(expected, PhoneTotalFile.Parse(typed));

    [Fact]
    public void A_damaged_file_reads_as_no_total()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "phone-total.txt"), "xyz");

        Assert.Null(PhoneTotalFile.Load(_folder));
    }
}
