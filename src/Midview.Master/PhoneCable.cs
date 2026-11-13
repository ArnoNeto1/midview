using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Midview.Protocol;

namespace Midview.Master;

// What can stop a phone from being prepared over the cable.
public enum CableProblem
{
    NoPhone, TooMany, Unauthorized, TooOld, SecurityOff, InstallOff,
    HasAccounts, HasUsers, AlreadySchool, InstallDeclined, InstallFailed, Refused, Lost,
    MiSignOutFailed,
}

// MiSignOut: only for a phone still in the school's Mi account (MiSignOut),
// last before the Owner step.
public enum CableStage { Checking, Installing, Wifi, Pairing, MiSignOut, Owner }

// Phone is set when the phone is ready; Problem when it is not. OwnerUnconfirmed:
// the cable dropped while the last command ran, so whether the phone became the
// school's own is not known yet (the phone shows it on its screen).
public sealed record CableResult(KnownMachine? Phone, CableProblem? Problem, bool OwnerUnconfirmed);

// Prepares one school phone over USB, driving adb in a fixed order:
// check, install, allow past the phone's cleaner, hand over the setup, make it
// the school's own. Nothing uses the cable after that last command.
public sealed partial class PhoneCable
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InstallTime = TimeSpan.FromMinutes(3);
    private static readonly Regex AccountsLine = new(@"Accounts:\s*(\d+)", RegexOptions.Compiled);
    // What adb says when the phone left the cable while a command ran.
    // "more than one device" is not here: it means the command never ran.
    private static readonly Regex GoneText = new(
        "device offline|no devices|device .* not found|device not found|error: closed|disconnected",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string Authority = "content://io.github.arnoneto1.midview.agent.setup";
    private const string AccessibilityService =
        PhoneProvisioning.AgentPackage + "/" + PhoneProvisioning.AgentPackage + ".screen.AccessibilityCapture";

    private readonly IAdb _adb;

    // The serial of the phone the last Check found. Every command after the
    // check names it ("-s"), so a second phone plugged in meanwhile is never
    // the one changed, nor makes adb refuse the command.
    private string? _serial;

    // The last Check let Mi accounts through (see Check(bool)).
    private bool MiAccountsLeft { get; set; }

    private static bool AnyAccount(string dumpsys) =>
        AccountsLine.Matches(dumpsys).Any(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) > 0);

    // How the Mi sign-out gets its clock: a fake one in the tests.
    internal Func<Func<string[], AdbOutput>, MiSignOut> MakeSignOut { get; init; } = MiSignOut.WithRealClock;

    public PhoneCable(IAdb adb)
    {
        _adb = adb;
    }

    private AdbOutput Run(TimeSpan timeout, params string[] args) =>
        _adb.Run(timeout, new[] { "-s", _serial! }.Concat(args).ToArray());

    private AdbOutput Shell(TimeSpan timeout, params string[] command) =>
        Run(timeout, new[] { "shell" }.Concat(command).ToArray());

    private string Prop(string name) => Shell(Short, "getprop", name).Text.Trim();

    // What "adb devices" lists: each serial with its state.
    private List<(string Serial, string State)> Listed()
    {
        var output = _adb.Run(Short, "devices");
        var devices = new List<(string Serial, string State)>();
        foreach (var line in output.Text.Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || line.StartsWith("List of", StringComparison.Ordinal) || line.StartsWith('*'))
            {
                continue;
            }
            devices.Add((parts[0], parts[1]));
        }
        return devices;
    }

    // The single phone on the cable, ready for commands, or the problem when
    // there is not exactly one.
    private (CableProblem? Problem, string? Serial) Devices()
    {
        var devices = Listed();
        if (devices.Count == 0)
        {
            return (CableProblem.NoPhone, null);
        }
        if (devices.Count > 1)
        {
            return (CableProblem.TooMany, null);
        }
        return devices[0].State switch
        {
            "device" => (null, devices[0].Serial),
            "unauthorized" => (CableProblem.Unauthorized, null),
            _ => (CableProblem.NoPhone, null),
        };
    }

    // Whether the phone being prepared is still on the cable, ready. Another
    // phone plugged in beside it changes nothing.
    private bool Present() => Listed().Any(d => d.Serial == _serial && d.State == "device");

    public CableProblem? Check() => Check(allowMiAccounts: false);

    // allowMiAccounts: with the school's Mi password saved, Mi accounts (and
    // only those) do not stop the check; MiAccountsLeft says they are there,
    // for Prepare to sign out at the end.
    private CableProblem? Check(bool allowMiAccounts)
    {
        MiAccountsLeft = false;
        var (problem, serial) = Devices();
        _serial = serial;
        if (problem is not null)
        {
            return problem;
        }

        if (!int.TryParse(Prop("ro.build.version.sdk"), NumberStyles.None, CultureInfo.InvariantCulture, out var sdk))
        {
            return CableProblem.NoPhone;
        }
        if (sdk < 29)
        {
            return CableProblem.TooOld;
        }

        if (Prop("ro.miui.ui.version.name").Length > 0)
        {
            if (Prop("persist.security.adbinput") != "1")
            {
                return CableProblem.SecurityOff;
            }
            if (Prop("persist.security.adbinstall") != "1")
            {
                return CableProblem.InstallOff;
            }
        }

        var accounts = Shell(Short, "dumpsys", "account").Text;
        if (AnyAccount(accounts))
        {
            if (!allowMiAccounts || !MiSignOut.OnlyXiaomi(accounts))
            {
                return CableProblem.HasAccounts;
            }
            MiAccountsLeft = true;
        }

        var users = Shell(Short, "pm", "list", "users").Text;
        if (users.Split("UserInfo{").Length - 1 > 1)
        {
            return CableProblem.HasUsers;
        }

        if (Shell(Short, "dpm", "list-owners").Text.Contains(PhoneProvisioning.AgentPackage, StringComparison.Ordinal))
        {
            return CableProblem.AlreadySchool;
        }
        return null;
    }

    // Apps that come with the phone, are heavy and that a class does not need
    // running unseen. Never Midview, the launcher, the keyboard, the dialer,
    // Google Play services, the Play Store, Chrome or Settings. Undone on the phone with
    // "cmd appops set <app> RUN_ANY_IN_BACKGROUND allow".
    internal static readonly IReadOnlyList<string> HeavyApps = new[]
    {
        "cn.wps.moffice_eng",
        "com.google.android.apps.docs",
        "com.miui.weather2",
        "com.miui.player",
        "com.miui.videoplayer",
        "com.mi.globalminusscreen",
    };

    // Apps removed for the phone's user ("pm uninstall --user 0"), checked
    // against the Universal Android Debloater list (all "Recommended" there,
    // or "Advanced" and of no use to a class) and tried on the school's
    // Redmi 10C on 2026-11-13: factory tests, Xiaomi and Google telemetry and
    // ads, cloud backup, Chinese payments, "Ok Google", the Google app (about
    // 285 MB of a 2.7 GB phone's memory). Also what kept running unseen on a
    // school phone with no SIM (the school's choice, 2026-11-13): Messages
    // and its chat process (about 71 MB), Android System Intelligence and the
    // setup wizard's "finish setting up" reminder. Never Midview, the
    // launcher, the keyboard, the dialer, the camera, the gallery, the
    // calculator, the file manager, the screen recorder, Chrome, Google Play
    // services, the Play Store, the installers, the VPN screens (the internet
    // block), what the Device Owner step needs, or Xiaomi's Find device (Mi
    // Cloud's service crashes at every start without its provider). A
    // system app comes back with "cmd package install-existing <app>" or a
    // factory reset.
    internal static readonly IReadOnlyList<string> RemovedApps = new[]
    {
        "android.autoinstalls.config.Xiaomi.model",
        "com.android.apps.tag",
        "com.android.bluetoothmidiservice",
        "com.android.calllogbackup",
        "com.android.dreams.basic",
        "com.android.dreams.phototable",
        "com.android.egg",
        "com.android.hotwordenrollment.okgoogle",
        "com.android.hotwordenrollment.xgoogle",
        "com.android.providers.partnerbookmarks",
        "com.android.theme.font.notoserifsource",
        "com.android.traceur",
        "com.android.wallpaper.livepicker",
        "com.android.wallpaperbackup",
        "com.bsp.catchlog",
        "com.fido.asm",
        "com.fingerprints.sensortesttool",
        "com.google.android.adservices.api",
        "com.google.android.apps.messaging",
        "com.google.android.apps.restore",
        "com.google.android.as",
        "com.google.android.federatedcompute",
        "com.google.android.feedback",
        "com.google.android.gms.location.history",
        "com.google.android.gms.supervision",
        "com.google.android.googlequicksearchbox",
        "com.google.android.ondevicepersonalization.services",
        "com.google.android.setupwizard",
        "com.google.android.onetimeinitializer",
        "com.google.android.printservice.recommendation",
        "com.google.mainline.adservices",
        "com.longcheertel.AutoTest",
        "com.longcheertel.cit",
        "com.longcheertel.sarauth",
        "com.mi.global.shop",
        "com.mi.globallayout",
        "com.mi.globalminusscreen",
        "com.miui.audiomonitor",
        "com.miui.backup",
        "com.miui.cleaner",
        "com.miui.cloudbackup",
        "com.miui.daemon",
        "com.miui.face",
        "com.miui.face.overlay.miui",
        "com.miui.fmservice",
        "com.miui.micloudsync",
        "com.miui.phrase",
        "com.miuix.editor",
        "com.modemdebug",
        "com.qti.qualcomm.deviceinfo",
        "com.qti.xdivert",
        "com.qualcomm.embms",
        "com.qualcomm.qti.devicestatisticsservice",
        "com.qualcomm.qti.performancemode",
        "com.qualcomm.qti.powersavemode",
        "com.qualcomm.qti.qms.service.trustzoneaccess",
        "com.qualcomm.qti.ridemodeaudio",
        "com.quicinc.voice.activation",
        "com.silead.fingerprint",
        "com.tencent.soter.soterserver",
        "com.wdstechnology.android.kryten",
        "com.xiaomi.barrage",
        "com.xiaomi.cameratools",
        "com.xiaomi.mtb",
        "com.xiaomi.payment",
        "com.xiaomi.simactivate.service",
        "com.xiaomi.xmsfkeeper",
        "de.telekom.tsc",
        "org.ifaa.aidl.manager",
        "org.mipay.android.manager",
    };

    // A lighter phone: no animations, the apps above removed, the heavy apps
    // still on it kept from running in the background, and the home screen's
    // gestures that open the removed Google app (a blank white screen) off.
    // Only from the cable, so right before the Device Owner step; a failure
    // does not stop the setup.
    private void Lighten()
    {
        foreach (var scale in new[] { "window_animation_scale", "transition_animation_scale", "animator_duration_scale" })
        {
            Shell(Short, "settings", "put", "global", scale, "0");
        }
        // MIUI's home: "Deslize para cima na Tela inicial para abrir a
        // pesquisa" and "Google Discover" (read and applied by the launcher
        // at once; unknown and harmless on other phones).
        Shell(Short, "settings", "put", "system", "QSB_SCROLL_ENTRANCE_STATUS", "0");
        Shell(Short, "settings", "put", "system", "open_personal_assistant", "0");
        var installed = Shell(Short, "pm", "list", "packages").Text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("package:", StringComparison.Ordinal))
            .Select(line => line["package:".Length..])
            .ToHashSet(StringComparer.Ordinal);
        foreach (var app in RemovedApps.Where(installed.Contains))
        {
            if (Shell(Short, "pm", "uninstall", "--user", "0", app).Text.Contains("Success", StringComparison.Ordinal))
            {
                installed.Remove(app);
            }
        }
        foreach (var app in HeavyApps.Where(installed.Contains))
        {
            Shell(Short, "cmd", "appops", "set", app, "RUN_ANY_IN_BACKGROUND", "ignore");
        }
    }

    // optimize: "Midview otimizado" lightens the phone (Lighten); "Só o
    // Midview" leaves the rest of it as it came. kind names the device
    // ("Tablet 07") here and on the device itself (the package's "kind").
    // miPassword: the school's Mi account password, saved on this PC; with
    // it, a phone whose only accounts are Mi accounts is prepared all the
    // same and signed out (MiSignOut) last, right before the Owner step,
    // instead of sent back to the step: MIUI may turn "Instalar via USB"
    // and "Depuração USB (configurações de segurança)" off once the account
    // leaves, and by then nothing else needs them.
    public CableResult Prepare(
        string apkPath, string number, string masterThumbprint, string ssid, string password, Action<CableStage> stage,
        bool optimize = true, AndroidDeviceKind kind = AndroidDeviceKind.Phone, string? miPassword = null)
    {
        static CableResult Fail(CableProblem problem) => new(null, problem, false);

        stage(CableStage.Checking);
        if (Check(allowMiAccounts: miPassword is not null) is { } problem)
        {
            return Fail(problem);
        }
        var signOut = MiAccountsLeft;

        stage(CableStage.Installing);
        var package = PhoneProvisioning.AgentPackage;
        var installed = Shell(Short, "pm", "list", "packages", package).Text
            .Split('\n').Any(l => l.Trim() == "package:" + package);
        if (installed)
        {
            // The phone is not the school's yet (Check), so this app is left
            // over: removed, not just cleared, so an install signed or
            // versioned differently cannot fail, and its update cannot start
            // the agent before the setup is handed over.
            Shell(Short, "pm", "uninstall", package);
        }

        // A Xiaomi asks "Instalar via USB" on its screen and says no by itself
        // after 10 s; with the screen off or locked it does not ask at all and
        // says no about a minute later. So the screen is woken and unlocked
        // (a phone being prepared has no screen lock) before installing.
        Shell(Short, "input", "keyevent", "KEYCODE_WAKEUP");
        Shell(Short, "wm", "dismiss-keyguard");

        var install = Run(InstallTime, "install", "-r", apkPath);
        if (install.TimedOut || !install.Text.Contains("Success", StringComparison.Ordinal))
        {
            // The "Instalar via USB" switch is on (Check read it), so a refusal
            // here is the phone's question left unanswered or answered no.
            // An install still running after the wait is held by Google Play
            // Protect's question ("Enviar o app para uma verificação de
            // segurança?"), which never answers itself.
            return Fail(install.TimedOut
                        || install.Text.Contains("INSTALL_FAILED_USER_RESTRICTED", StringComparison.Ordinal)
                ? CableProblem.InstallDeclined
                : CableProblem.InstallFailed);
        }

        // Keeps the phone's cleaner from killing the app. A failure here does not stop the setup.
        Shell(Short, "cmd", "appops", "set", package, "10008", "allow");
        Shell(Short, "cmd", "deviceidle", "whitelist", "+" + package);
        // "Recolher arquivos": what other apps saved in Download/Midview is
        // hidden from the agent without "acesso a todos os arquivos", which a
        // Device Owner cannot grant itself (Android 11 or newer); Android 10
        // has the older permissions instead (an error on the other one is
        // expected and does not stop the setup).
        Shell(Short, "cmd", "appops", "set", "--uid", package, "MANAGE_EXTERNAL_STORAGE", "allow");
        Shell(Short, "pm", "grant", package, "android.permission.READ_EXTERNAL_STORAGE");
        Shell(Short, "pm", "grant", package, "android.permission.WRITE_EXTERNAL_STORAGE");
        // The screen reader the agent shows the screen with (Android 11 or
        // newer): without it the phone asks "Iniciar agora" before the
        // teacher sees its screen. Only from the cable, so before the
        // Device Owner step; a failure does not stop the setup either.
        Shell(Short, "settings", "put", "secure", "enabled_accessibility_services", AccessibilityService);
        Shell(Short, "settings", "put", "secure", "accessibility_enabled", "1");

        stage(CableStage.Wifi);
        var payload = CableSetupPayload.Encode(number, masterThumbprint, ssid, password, kind);
        var call = Shell(
            Short, "content", "call", "--uri", Authority, "--method", "prepare", "--extra", "setup:s:" + payload);
        var bundle = ParseBundle(call.Text);
        if (!bundle.TryGetValue("ok", out var ok))
        {
            // No answer from the door: the cable dropped, or the app did not answer.
            return Fail(Present() ? CableProblem.Refused : CableProblem.Lost);
        }
        if (!ok.Equals("true", StringComparison.OrdinalIgnoreCase)
            || !bundle.TryGetValue("thumbprint", out var thumbprint) || thumbprint.Length == 0)
        {
            return Fail(CableProblem.Refused);
        }

        stage(CableStage.Pairing);
        var phone = new KnownMachine(
            PhoneNumber.NameOf(number, kind), "0.0.0.0", thumbprint, thumbprint, null, DeviceKind.Android);

        if (signOut)
        {
            stage(CableStage.MiSignOut);
            // Only the accounts are looked at after it: the switches are no
            // longer needed. A failure leaves the app installed and the
            // phone not the school's; "Tentar de novo" starts over.
            if (!MakeSignOut(command => Shell(Short, command)).Run(miPassword!)
                || AnyAccount(Shell(Short, "dumpsys", "account").Text))
            {
                return Fail(Present() ? CableProblem.MiSignOutFailed : CableProblem.Lost);
            }
        }

        stage(CableStage.Owner);
        // Last before the Owner step, after the Mi sign-out: it removes Xiaomi
        // cloud apps (com.miui.micloudsync, com.miui.cloudbackup) the
        // sign-out may still need. None of it depends on MIUI's security
        // switches (pm uninstall --user 0, settings put, cmd appops).
        if (optimize)
        {
            Lighten();
        }

        if (!Present())
        {
            return Fail(CableProblem.Lost);
        }

        var owner = Shell(Short, "dpm", "set-device-owner", PhoneProvisioning.AdminComponent);
        // From here on nothing else touches the cable.
        if (owner.Text.Contains("Success", StringComparison.Ordinal))
        {
            return new CableResult(phone, null, false);
        }
        if (owner.Text.Contains("MANAGE_DEVICE_ADMINS", StringComparison.Ordinal))
        {
            return Fail(CableProblem.SecurityOff);
        }
        if (owner.Text.Contains("already some accounts", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(CableProblem.HasAccounts);
        }
        if (owner.Text.Contains("already several users", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(CableProblem.HasUsers);
        }
        if (owner.TimedOut || owner.Text.Trim().Length == 0 || GoneText.IsMatch(owner.Text))
        {
            return new CableResult(phone, null, true);
        }
        return Fail(CableProblem.Refused);
    }

    // "Result: Bundle[{ok=true, thumbprint=AB12, name=Celular 07}]" into its keys.
    // Bundle.toString does not promise an order, so every key is read by name.
    internal static Dictionary<string, string> ParseBundle(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        const string Opening = "Bundle[{";
        var open = text.IndexOf(Opening, StringComparison.Ordinal);
        if (open < 0)
        {
            return values;
        }
        var body = text[(open + Opening.Length)..];
        var close = body.IndexOf("}]", StringComparison.Ordinal);
        if (close >= 0)
        {
            body = body[..close];
        }
        // A value can hold ", " (a name), so a new pair starts only at ", key=" of a known key.
        foreach (var part in Regex.Split(body, @",\s*(?=(?:ok|thumbprint|name|error)=)"))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                values[part[..eq].Trim()] = part[(eq + 1)..].Trim();
            }
        }
        return values;
    }

    public static (string Text, string? BackToStep) Describe(CableProblem p) => p switch
    {
        CableProblem.NoPhone => ("O celular não está no cabo.", "cabo"),
        CableProblem.TooMany => ("Há mais de um celular no cabo. Deixe só um.", "cabo"),
        CableProblem.Unauthorized => ("Falta tocar em Permitir no celular.", "cabo"),
        CableProblem.TooOld => ("Este celular é antigo demais para o Midview (precisa do Android 10 ou mais novo).", null),
        CableProblem.SecurityOff => ("A chave \"Depuração USB (configurações de segurança)\" está desligada.", "chaves"),
        CableProblem.InstallOff => ("A chave \"Instalar via USB\" está desligada.", "chaves"),
        CableProblem.HasAccounts => ("Ainda há uma conta neste celular.", "sair-contas"),
        CableProblem.HasUsers => ("O celular tem um Segundo espaço ou Apps duplos. Apague-os nas Configurações e tente de novo.", null),
        CableProblem.AlreadySchool => ("Este celular já é da escola. Para prepará-lo de novo, use \"Apagar e preparar de novo\" no Midview.", null),
        CableProblem.InstallDeclined => ("O celular não deixou instalar. Deixe a tela dele acesa e responda às perguntas: em \"Enviar o app para uma verificação de segurança?\", toque em Não enviar; em \"Instalar via USB\", toque em Instalar (não em Recusar) antes de 10 segundos.", "cabo"),
        CableProblem.InstallFailed => ("O Midview não foi instalado no celular. Se apareceu uma pergunta no celular, toque em Instalar e tente de novo.", "cabo"),
        CableProblem.Refused => ("O celular recusou a preparação. Tente de novo.", "cabo"),
        CableProblem.Lost => ("O cabo soltou no meio. Ligue de novo e tente outra vez.", "cabo"),
        CableProblem.MiSignOutFailed => ("O Midview não conseguiu sair da conta Mi sozinho. Saia dela no celular e tente de novo.", "sair-contas"),
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };
}
