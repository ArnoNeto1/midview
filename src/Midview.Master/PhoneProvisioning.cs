namespace Midview.Master;

// What it takes to turn a factory-reset Android phone into a Midview phone:
// the Midview app, where to download it, and the gates that say the phone
// preparation is ready to be offered.
public static class PhoneProvisioning
{
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

    // Second gate: the phone is prepared over a USB cable (the cable path of
    // the assistant). Until that path is built, the wizard stays hidden.
    public const bool CableReady = false;

    // The wizard button, the "Preparar celular..." link and the "Apagar e
    // preparar de novo" menu item need both gates.
    public static bool WizardReady => WizardReadyWith(SignatureChecksum, CableReady);

    public static bool WizardReadyWith(string checksum, bool cableReady) =>
        cableReady && IsReadyWith(checksum);
}
