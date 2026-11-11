using System.Text.Json;
using Midview.Master;
using Xunit;

public class PhoneProvisioningTests
{
    private const string Prefix = "android.app.extra.";

    [Fact]
    public void Qr_json_has_every_provisioning_key()
    {
        using var doc = JsonDocument.Parse(PhoneProvisioning.QrJson("Lab", "senha123"));
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
    public void Password_with_quotes_accents_and_semicolon_round_trips()
    {
        const string password = "a\"b;ç";

        using var doc = JsonDocument.Parse(PhoneProvisioning.QrJson("Lab ç", password));

        Assert.Equal(password, doc.RootElement.GetProperty(Prefix + "PROVISIONING_WIFI_PASSWORD").GetString());
        Assert.Equal("Lab ç", doc.RootElement.GetProperty(Prefix + "PROVISIONING_WIFI_SSID").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_ssid_is_refused(string ssid)
    {
        Assert.Throws<ArgumentException>(() => PhoneProvisioning.QrJson(ssid, "x"));
    }
}
