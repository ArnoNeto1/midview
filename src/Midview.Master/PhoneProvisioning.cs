using System.Text.Json;
using System.Text.Json.Nodes;

namespace Midview.Master;

// How a phone becomes a Midview phone. Two separate switches:
// - SignatureChecksum (IsReady): the phone app is published. It lets CI attach
//   MidviewAgent.apk to the release and "Atualizar os celulares" work.
// - QrEnabled: the Master offers "Preparar celular...", the QR a factory-reset
//   phone scans on its first screen. Off: see QrEnabled.
// Which of the two the Master offers is Setup (PhoneSetupOffer).
public enum PhoneSetupOffer
{
    // The phone app is not published yet: "Celulares: em breve".
    Soon = 0,

    // Prepared by USB cable (adb, dpm set-device-owner); the Master points to
    // the README's steps.
    Cable = 1,

    // The "Preparar celular..." QR window.
    Qr = 2,
}

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

    // Off on purpose, whatever SignatureChecksum says. Google lets only the
    // device-policy apps on its approved list in through the QR of the first
    // screen (6 taps), and Midview is not on it: the phone downloads the app,
    // refuses it and says "Não foi possível configurar o dispositivo". So the
    // phones are prepared by USB cable (README, "Celulares"). Turn it on only
    // if Google approves the app.
    public const bool QrEnabled = false;

    public static PhoneSetupOffer Setup => SetupWith(IsReady, QrEnabled);

    public static PhoneSetupOffer SetupWith(bool appReady, bool qrEnabled) =>
        !appReady ? PhoneSetupOffer.Soon : qrEnabled ? PhoneSetupOffer.Qr : PhoneSetupOffer.Cable;

    // The README's "Celulares" section, with the cable steps.
    public const string CableStepsUrl = "https://github.com/ArnoNeto1/midview#celulares";

    // Said where the room has no phone yet (the grid, the Telas menu).
    public static string NoPhoneYet(PhoneSetupOffer setup) => setup switch
    {
        PhoneSetupOffer.Qr => "Nenhum celular nesta sala ainda. Para colocar o primeiro, abra \"Máquinas da sala\" e use \"Preparar celular\".",
        PhoneSetupOffer.Cable => "Nenhum celular nesta sala ainda. O celular é preparado pelo cabo USB: veja como em \"Máquinas da sala\" > \"Procurar e parear\".",
        _ => "Nenhum celular nesta sala ainda.",
    };

    // The QR's JSON (only with QrEnabled). Throws ArgumentException when the Wi-Fi name is empty;
    // quotes, accents and ";" in the name or password are escaped by the JSON.
    public static string QrJson(string ssid, string password)
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
        };
        return root.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
