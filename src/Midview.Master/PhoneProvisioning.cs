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

    // The SHA-256 of the release key's signing certificate, Base64 URL-safe
    // without "=" (the key in Documents\Midview-chave-android). android.yml
    // refuses to publish an APK whose certificate gives anything else.
    public const string SignatureChecksum = "JSlyorR7IjV9QQ2iePjvQskOpXXyTRL97n-j8VaV70o";

    // The Master's phone preparation is offered only once the real checksum is
    // in: with the placeholder there is no published app to provision.
    public static bool IsReady => IsReadyWith(SignatureChecksum);

    public static bool IsReadyWith(string checksum) =>
        !string.IsNullOrWhiteSpace(checksum) && checksum != "PREENCHER-NA-TASK-9";

    // Second gate: the phone is prepared over a USB cable (the cable path of
    // the assistant), built and tried on the school's Redmi 10C.
    public const bool CableReady = true;

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
    public static string NoPhoneYet(PhoneSetupOffer setup) => NoDeviceYet(AndroidDeviceKind.Phone, setup);

    // The same for a tablet ("Tablets" in the Telas menu and its empty grid).
    public static string NoDeviceYet(AndroidDeviceKind kind, PhoneSetupOffer setup)
    {
        var none = $"Nenhum {DeviceSetupText.Word(kind)} nesta sala ainda.";
        return setup switch
        {
            PhoneSetupOffer.Wizard => $"{none} Para colocar o primeiro, escolha \"{FilterName(kind)}\" e use \"Preparar {DeviceSetupText.Words(kind)}\".",
            PhoneSetupOffer.Cable => $"{none} O {DeviceSetupText.Word(kind)} é preparado pelo cabo USB: veja como em \"Máquinas da sala\" > \"{DeviceSetupText.Heading(kind)}\".",
            _ => none,
        };
    }

    // The "Telas" choice that shows the devices of that kind alone.
    private static string FilterName(AndroidDeviceKind kind) => kind == AndroidDeviceKind.Tablet ? "Tablets" : "Celulares";
}
