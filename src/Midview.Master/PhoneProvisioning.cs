namespace Midview.Master;

// How a phone becomes a Midview phone. Two gates:
// - SignatureChecksum (IsReady): the phone app is published. It lets CI attach
//   MidviewAgent.apk to the release and "Atualizar os celulares" work.
// - CableReady: the "Preparar celulares" assistant (PreparePhonesWindow), which
//   prepares a factory-reset phone over a USB cable, is built.
// The QR of the phone's first screen is gone: Google lets only the
// device-policy apps on its approved list in through it, and Midview is not
// on it. Which of the gates the Master offers is Setup (PhoneSetupOffer).
public enum PhoneSetupOffer
{
    // The phone app is not published yet: "Celulares: em breve".
    Soon = 0,

    // The app is published but the assistant is not out yet: the phone is
    // prepared by USB cable by hand (adb, dpm set-device-owner), and the
    // Master points to the README's steps.
    Cable = 1,

    // The "Preparar celulares" assistant (WizardReady).
    Wizard = 2,
}

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

    public static PhoneSetupOffer Setup => SetupWith(IsReady, WizardReady);

    // The assistant needs the app published too (WizardReady implies IsReady).
    public static PhoneSetupOffer SetupWith(bool appReady, bool wizardReady) =>
        !appReady ? PhoneSetupOffer.Soon : wizardReady ? PhoneSetupOffer.Wizard : PhoneSetupOffer.Cable;

    // The README's "Celulares" section, with the cable steps.
    public const string CableStepsUrl = "https://github.com/ArnoNeto1/midview#celulares";

    // Said where the room has no phone yet (the grid, the Telas menu).
    public static string NoPhoneYet(PhoneSetupOffer setup) => setup switch
    {
        PhoneSetupOffer.Wizard => "Nenhum celular nesta sala ainda. Para colocar o primeiro, escolha \"Celulares\" e use \"Preparar celulares\".",
        PhoneSetupOffer.Cable => "Nenhum celular nesta sala ainda. O celular é preparado pelo cabo USB: veja como em \"Máquinas da sala\" > \"Procurar e parear\".",
        _ => "Nenhum celular nesta sala ainda.",
    };
}
