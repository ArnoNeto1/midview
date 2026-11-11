using System.Security.Cryptography;
using System.Text;
using Midview.Protocol;
using Xunit;

public class PhoneClaimTests
{
    [Fact]
    public void New_tokens_are_43_url_safe_chars_and_differ()
    {
        var a = PhoneClaim.NewToken();
        Assert.Matches("^[A-Za-z0-9_-]{43}$", a);
        Assert.NotEqual(a, PhoneClaim.NewToken());
    }

    [Fact]
    public void Claim_id_is_first_16_upper_hex_of_sha256()
        => Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("abc")))[..16], PhoneClaim.ClaimId("abc"));

    [Fact]
    public void Claim_request_round_trips_and_rejects_empty_token()
    {
        Assert.Equal(new PhoneClaimRequest("t", "PROF"), PhoneClaimRequest.Decode(new PhoneClaimRequest("t", "PROF").Encode()));
        Assert.Null(PhoneClaimRequest.Decode(Encoding.UTF8.GetBytes("{\"token\":\"\",\"teacher\":\"x\"}")));
        Assert.Null(PhoneClaimRequest.Decode(Encoding.UTF8.GetBytes("não é json")));
    }

    [Fact]
    public void Claim_request_rejects_payloads_over_4096_bytes()
    {
        var big = Encoding.UTF8.GetBytes("{\"token\":\"t\",\"teacher\":\"" + new string('x', 5000) + "\"}");
        Assert.Null(PhoneClaimRequest.Decode(big));
    }

    [Fact]
    public void Claim_messages_have_the_agreed_numbers()
    {
        Assert.Equal(93, (int)MessageType.ClaimPhone);
        Assert.Equal(94, (int)MessageType.WipePhone);
    }
}
