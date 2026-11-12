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
    public void Claim_request_round_trips_and_rejects_a_bad_nonce()
    {
        var nonce = PhoneClaim.NewNonce();
        Assert.Matches("^[A-Za-z0-9_-]{43}$", nonce);
        Assert.Equal(new PhoneClaimRequest(nonce, "PROF"), PhoneClaimRequest.Decode(new PhoneClaimRequest(nonce, "PROF").Encode()));
        Assert.Contains("\"nonce\"", Encoding.UTF8.GetString(new PhoneClaimRequest(nonce, "PROF").Encode()));
        Assert.Null(PhoneClaimRequest.Decode(new PhoneClaimRequest("curto", "PROF").Encode()));
        Assert.Null(PhoneClaimRequest.Decode(new PhoneClaimRequest(new string('+', 43), "PROF").Encode()));
        Assert.Null(PhoneClaimRequest.Decode(Encoding.UTF8.GetBytes("{\"nonce\":\"" + nonce + "\"}")));
        Assert.Null(PhoneClaimRequest.Decode(Encoding.UTF8.GetBytes("não é json")));
    }

    [Fact]
    public void Claim_request_rejects_payloads_over_4096_bytes()
    {
        var big = Encoding.UTF8.GetBytes("{\"nonce\":\"" + PhoneClaim.NewNonce() + "\",\"teacher\":\"" + new string('x', 5000) + "\"}");
        Assert.Null(PhoneClaimRequest.Decode(big));
    }

    [Fact]
    public void Proof_matches_the_fixed_vector_and_ignores_thumbprint_case_and_spaces()
    {
        var proof = PhoneClaim.Proof("token-123", "nonce-abc", "aa11", "BB22");
        Assert.Equal("WSiEijnq4JkvauANCY9jxAA-15IWfTZOg00j1DJWdLE", proof);
        Assert.Equal(proof, PhoneClaim.Proof("token-123", "nonce-abc", " AA11 ", "bb22"));
        Assert.NotEqual(proof, PhoneClaim.Proof("token-124", "nonce-abc", "AA11", "BB22"));
        Assert.NotEqual(proof, PhoneClaim.Proof("token-123", "nonce-abd", "AA11", "BB22"));
    }

    [Fact]
    public void Claim_messages_have_the_agreed_numbers()
    {
        Assert.Equal(101, (int)MessageType.ClaimPhone);
        Assert.Equal(100, (int)MessageType.WipePhone);
    }
}
