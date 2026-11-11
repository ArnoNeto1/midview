using Midview.Master;
using Xunit;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("7", "07")]
    [InlineData(" 12 ", "12")]
    [InlineData("105", "105")]
    [InlineData("0", "00")]
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
}
