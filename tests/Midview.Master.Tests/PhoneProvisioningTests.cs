using System.Text.Json;
using Midview.Master;
using Xunit;

public class PhoneProvisioningTests
{
    private const string Prefix = "android.app.extra.";

    [Fact]
    public void Qr_json_has_every_provisioning_key()
    {
        using var doc = JsonDocument.Parse(PhoneProvisioning.QrJson("Lab", "senha123", "07", "tok", "AB"));
        var root = doc.RootElement;

        Assert.Equal("io.github.arnoneto1.midview.agent/.AdminReceiver", root.GetProperty(Prefix + "PROVISIONING_DEVICE_ADMIN_COMPONENT_NAME").GetString());
        Assert.Equal("https://github.com/ArnoNeto1/midview/releases/latest/download/MidviewAgent.apk", root.GetProperty(Prefix + "PROVISIONING_DEVICE_ADMIN_PACKAGE_DOWNLOAD_LOCATION").GetString());
        Assert.Equal(PhoneProvisioning.SignatureChecksum, root.GetProperty(Prefix + "PROVISIONING_DEVICE_ADMIN_SIGNATURE_CHECKSUM").GetString());
        Assert.Equal("Lab", root.GetProperty(Prefix + "PROVISIONING_WIFI_SSID").GetString());
        Assert.Equal("senha123", root.GetProperty(Prefix + "PROVISIONING_WIFI_PASSWORD").GetString());
        Assert.Equal("WPA", root.GetProperty(Prefix + "PROVISIONING_WIFI_SECURITY_TYPE").GetString());
        var leave = root.GetProperty(Prefix + "PROVISIONING_LEAVE_ALL_SYSTEM_APPS_ENABLED");
        Assert.Equal(JsonValueKind.True, leave.ValueKind);
    }

    [Fact]
    public void Qr_carries_number_token_and_master_in_admin_extras()
    {
        using var doc = JsonDocument.Parse(PhoneProvisioning.QrJson("Lab", "senha123", "07", "tok-abc", "abcdef0123"));
        var root = doc.RootElement;

        var extras = root.GetProperty(Prefix + "PROVISIONING_ADMIN_EXTRAS_BUNDLE");
        Assert.Equal(JsonValueKind.Object, extras.ValueKind);
        Assert.Equal(3, extras.EnumerateObject().Count());
        Assert.Equal("07", extras.GetProperty("midview.number").GetString());
        Assert.Equal("tok-abc", extras.GetProperty("midview.token").GetString());
        Assert.Equal("ABCDEF0123", extras.GetProperty("midview.master").GetString());
        Assert.Equal("Lab", root.GetProperty(Prefix + "PROVISIONING_WIFI_SSID").GetString());
        Assert.Equal("senha123", root.GetProperty(Prefix + "PROVISIONING_WIFI_PASSWORD").GetString());
        Assert.Equal(PhoneProvisioning.AdminComponent, root.GetProperty(Prefix + "PROVISIONING_DEVICE_ADMIN_COMPONENT_NAME").GetString());
    }

    [Fact]
    public void Claim_lifetime_is_one_hour() =>
        Assert.Equal(TimeSpan.FromHours(1), PhoneProvisioning.ClaimLifetime);

    [Fact]
    public void Password_with_quotes_accents_and_semicolon_round_trips()
    {
        const string password = "a\"b;ç";

        using var doc = JsonDocument.Parse(PhoneProvisioning.QrJson("Lab ç", password, "07", "tok", "AB"));

        Assert.Equal(password, doc.RootElement.GetProperty(Prefix + "PROVISIONING_WIFI_PASSWORD").GetString());
        Assert.Equal("Lab ç", doc.RootElement.GetProperty(Prefix + "PROVISIONING_WIFI_SSID").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_ssid_is_refused(string ssid)
    {
        Assert.Throws<ArgumentException>(() => PhoneProvisioning.QrJson(ssid, "x", "07", "tok", "AB"));
    }

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
    public void Wizard_needs_the_checksum_and_the_claims(string checksum, bool claimsReady, bool expected) =>
        Assert.Equal(expected, PhoneProvisioning.WizardReadyWith(checksum, claimsReady));

    [Fact]
    public void WizardReady_follows_both_constants() =>
        Assert.Equal(
            PhoneProvisioning.WizardReadyWith(PhoneProvisioning.SignatureChecksum, PhoneProvisioning.ClaimsReady),
            PhoneProvisioning.WizardReady);
}
