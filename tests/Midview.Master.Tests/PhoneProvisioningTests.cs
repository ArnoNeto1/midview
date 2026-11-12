using Midview.Master;
using Xunit;

public class PhoneProvisioningTests
{
    private const string Placeholder = "PREENCHER-NA-TASK-9";

    // The signing checksum is a SHA-256: 32 bytes in Base64 URL-safe without
    // "=" are 43 characters. Skipped while the value is still the placeholder.
    [Fact]
    public void Signature_checksum_is_base64_url_safe_sha256()
    {
        if (PhoneProvisioning.SignatureChecksum == Placeholder)
        {
            return;
        }
        Assert.Matches("^[A-Za-z0-9_-]{43}$", PhoneProvisioning.SignatureChecksum);
    }

    // Opt-in guard: with MIDVIEW_RELEASE set (no workflow sets it; android.yml
    // and pacote.yml gate on the placeholder themselves), a placeholder
    // checksum fails, since prepared phones would refuse the app.
    [Fact]
    public void Placeholder_checksum_does_not_ship_in_a_release_build()
    {
        var release = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MIDVIEW_RELEASE"));
        Assert.False(release && PhoneProvisioning.SignatureChecksum == Placeholder,
            "PhoneProvisioning.SignatureChecksum ainda é o texto provisório; fixe o checksum do APK assinado antes de publicar.");
    }

    [Fact]
    public void Placeholder_checksum_is_not_ready() =>
        Assert.False(PhoneProvisioning.IsReadyWith("PREENCHER-NA-TASK-9"));

    [Fact]
    public void Empty_checksum_is_not_ready() =>
        Assert.False(PhoneProvisioning.IsReadyWith(""));

    [Fact]
    public void Real_looking_checksum_is_ready() =>
        Assert.True(PhoneProvisioning.IsReadyWith(new string('A', 43)));

    [Fact]
    public void IsReady_follows_the_constant() =>
        Assert.Equal(PhoneProvisioning.IsReadyWith(PhoneProvisioning.SignatureChecksum), PhoneProvisioning.IsReady);

    [Theory]
    [InlineData("PREENCHER-NA-TASK-9", false, false)]
    [InlineData("PREENCHER-NA-TASK-9", true, false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false, false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true, true)]
    public void Wizard_needs_both_gates(string checksum, bool cableReady, bool expected) =>
        Assert.Equal(expected, PhoneProvisioning.WizardReadyWith(checksum, cableReady));

    [Fact]
    public void WizardReady_follows_both_constants() =>
        Assert.Equal(
            PhoneProvisioning.WizardReadyWith(PhoneProvisioning.SignatureChecksum, PhoneProvisioning.CableReady),
            PhoneProvisioning.WizardReady);
}
