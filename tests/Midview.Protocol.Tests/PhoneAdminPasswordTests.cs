using System.Text;
using Midview.Protocol;
using Xunit;

public class PhoneAdminPasswordTests
{
    private static readonly byte[] Salt = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();

    [Theory]
    [InlineData("abc123")]
    [InlineData("Escola2026")]
    [InlineData("123456")]
    [InlineData("ABCDEFGHIJKL")]
    public void SixToTwelveLettersOrDigits_AreValid(string password)
    {
        Assert.True(PhoneAdminPassword.IsValid(password));
        var stored = PhoneAdminPassword.Decode(PhoneAdminPassword.Encode(password, Salt, 10_000));
        Assert.NotNull(stored);
        Assert.True(PhoneAdminPassword.Verify(password, stored));
    }

    [Theory]
    [InlineData("1234")] // The screen PIN's length.
    [InlineData("abc12")]
    [InlineData("abcdefghijklm")]
    [InlineData("abc 123")]
    [InlineData("senha-1")]
    [InlineData("çãoabc")]
    [InlineData("１２３４５６")] // Fullwidth digits.
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_IsRefused(string? password)
    {
        Assert.False(PhoneAdminPassword.IsValid(password));
        if (password is not null)
        {
            Assert.Throws<ArgumentException>(() => PhoneAdminPassword.Encode(password));
        }
    }

    [Fact]
    public void Verify_RefusesAnotherPassword()
    {
        var stored = PhoneAdminPassword.Decode(PhoneAdminPassword.Encode("abc123", Salt, 10_000))!;
        Assert.False(PhoneAdminPassword.Verify("abc124", stored));
        Assert.False(PhoneAdminPassword.Verify("ABC123", stored));
        Assert.False(PhoneAdminPassword.Verify("", stored));
    }

    // The payload carries no password: version, iterations, salt and hash.
    // Same bytes as PhoneAdminPasswordTest.kt (the Android agent).
    [Fact]
    public void Encode_IsVersionIterationsSaltAndHash()
    {
        var payload = PhoneAdminPassword.Encode("abc123", Salt, 10_000);

        Assert.Equal(PhoneAdminPassword.PayloadLength, payload.Length);
        Assert.Equal(53, payload.Length);
        Assert.Equal(new byte[] { 1, 0, 0, 0x27, 0x10 }, payload[..5]);
        Assert.Equal(Salt, payload[5..21]);
        Assert.Equal(KnownHash, Convert.ToHexString(payload[21..]));
        Assert.DoesNotContain("abc123", Encoding.ASCII.GetString(payload));
    }

    // PBKDF2-HMAC-SHA256 of "abc123", salt 00..0F, 10 000 iterations.
    internal const string KnownHash = "42FB757417EDCFAB297FE3D49C0C3EF0057AD45E1B331AD5921B203983BBA499";

    // RFC 7914, section 11: PBKDF2-HMAC-SHA256 ("passwd", "salt", 1), its first 32 bytes.
    [Fact]
    public void Hash_IsPbkdf2HmacSha256() =>
        Assert.Equal(
            "55AC046E56E3089FEC1691C22544B605F94185216DDE0465E68B9D57C20DACBC",
            Convert.ToHexString(PhoneAdminPassword.Hash("passwd", Encoding.ASCII.GetBytes("salt"), 1)));

    [Fact]
    public void A_fresh_salt_each_send()
    {
        var first = PhoneAdminPassword.Decode(PhoneAdminPassword.Encode("abc123"))!;
        var second = PhoneAdminPassword.Decode(PhoneAdminPassword.Encode("abc123"))!;

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.Equal(PhoneAdminPassword.DefaultIterations, first.Iterations);
        Assert.True(PhoneAdminPassword.Verify("abc123", second));
    }

    [Fact]
    public void Decode_RefusesWhatThisVersionCannotUse()
    {
        var good = PhoneAdminPassword.Encode("abc123", Salt, 10_000);

        Assert.Null(PhoneAdminPassword.Decode(Array.Empty<byte>()));
        Assert.Null(PhoneAdminPassword.Decode(good[..52]));
        Assert.Null(PhoneAdminPassword.Decode(good.Append((byte)0).ToArray()));
        var otherVersion = (byte[])good.Clone();
        otherVersion[0] = 2;
        Assert.Null(PhoneAdminPassword.Decode(otherVersion));
        var tooFew = (byte[])good.Clone();
        tooFew[3] = 0;
        tooFew[4] = 1;
        Assert.Null(PhoneAdminPassword.Decode(tooFew));
        var tooMany = (byte[])good.Clone();
        tooMany[1] = 0x7F;
        Assert.Null(PhoneAdminPassword.Decode(tooMany));
    }

    [Fact]
    public void Invalid_IsTheTeachersText() =>
        Assert.Equal("a senha tem que ter de 6 a 12 letras ou números", PhoneAdminPassword.Invalid);
}
