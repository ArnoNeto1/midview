using System.Text.Json;
using System.Text.Json.Nodes;

namespace Midview.Master;

// The text of the QR code a factory-reset Android phone scans on its first
// screen to become a Midview phone: it names the Midview app, where to
// download it, the lab Wi-Fi to join first, and keeps the phone's own apps.
public static class PhoneProvisioning
{
    private const string Extra = "android.app.extra.";

    public const string AgentPackage = "io.github.arnoneto1.midview.agent";

    public const string AdminComponent = AgentPackage + "/.AdminReceiver";

    public const string ApkUrl = "https://github.com/ArnoNeto1/midview/releases/latest/download/MidviewAgent.apk";

    // Placeholder: a later task (the signing of the phone app) replaces it with
    // the SHA-256 of the signing certificate, Base64 URL-safe without "=".
    public const string SignatureChecksum = "PREENCHER-NA-TASK-9";

    // The Master's phone preparation is offered only once the real checksum is
    // in: with the placeholder there is no published app to provision.
    public static bool IsReady => IsReadyWith(SignatureChecksum);

    public static bool IsReadyWith(string checksum) =>
        !string.IsNullOrWhiteSpace(checksum) && checksum != "PREENCHER-NA-TASK-9";

    // How long the one-use proof in a QR stays valid.
    public static readonly TimeSpan ClaimLifetime = TimeSpan.FromHours(1);

    // The QR's JSON, with the phone's number, the one-use token and the
    // Master's certificate thumbprint handed to the phone app as admin extras.
    // Throws ArgumentException when the Wi-Fi name is empty; quotes, accents
    // and ";" in the name or password are escaped by the JSON.
    public static string QrJson(string ssid, string password, string number, string token, string masterThumbprint)
    {
        if (string.IsNullOrWhiteSpace(ssid))
        {
            throw new ArgumentException("O nome do Wi-Fi não pode ficar vazio.", nameof(ssid));
        }
        var root = new JsonObject
        {
            [Extra + "PROVISIONING_DEVICE_ADMIN_COMPONENT_NAME"] = AdminComponent,
            [Extra + "PROVISIONING_DEVICE_ADMIN_PACKAGE_DOWNLOAD_LOCATION"] = ApkUrl,
            [Extra + "PROVISIONING_DEVICE_ADMIN_SIGNATURE_CHECKSUM"] = SignatureChecksum,
            [Extra + "PROVISIONING_WIFI_SSID"] = ssid,
            [Extra + "PROVISIONING_WIFI_PASSWORD"] = password ?? "",
            [Extra + "PROVISIONING_WIFI_SECURITY_TYPE"] = "WPA",
            [Extra + "PROVISIONING_LEAVE_ALL_SYSTEM_APPS_ENABLED"] = true,
            [Extra + "PROVISIONING_ADMIN_EXTRAS_BUNDLE"] = new JsonObject
            {
                ["midview.number"] = number,
                ["midview.token"] = token,
                ["midview.master"] = masterThumbprint.ToUpperInvariant(),
            },
        };
        return root.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
