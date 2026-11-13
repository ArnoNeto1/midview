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
    MiSignOutFailed, ScreenLocked,
}

// HomeScreen: only for a Xiaomi phone in "Midview otimizado" (PhoneHomeScreen).
// MiSignOut: only for a phone still in the school's Mi account (MiSignOut),
// last before the Owner step.
public enum CableStage { Checking, Installing, Wifi, Pairing, HomeScreen, MiSignOut, Owner }

// Phone is set when the phone is ready; Problem when it is not. OwnerUnconfirmed:
// the cable dropped while the last command ran, so whether the phone became the
// school's own is not known yet (the phone shows it on its screen).
// HomeByHand: the cable could not arrange the phone's home screen; the
// teacher does it on the phone (PhoneSetupGuide.HomeScreenByHand).
public sealed record CableResult(KnownMachine? Phone, CableProblem? Problem, bool OwnerUnconfirmed, bool HomeByHand = false);

// Prepares one school phone over USB, driving adb in a fixed order:
// check, install, allow past the phone's cleaner, hand over the setup, make it
// the school's own. Nothing uses the cable after that last command.
public sealed partial class PhoneCable
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InstallTime = TimeSpan.FromMinutes(3);
    private static readonly Regex AccountsLine = new(@"Accounts:\s*(\d+)", RegexOptions.Compiled);
    // The keyguard's own state in "dumpsys window policy" (Android 10 to 14):
    // "secure=true" while a PIN, pattern or password is set.
    private static readonly Regex SecureKeyguard = new(@"^\s*secure=true\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
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

    // The last Check found a Xiaomi phone (MIUI).
    private bool Miui { get; set; }

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
        Miui = false;
        _localAuthenticators = Array.Empty<string>();
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

        Miui = Prop("ro.miui.ui.version.name").Length > 0;
        if (Miui)
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

        // The phone's own local accounts (LocalAccounts) do not count. Mi
        // accounts are let through only when every other account is one (a
        // Google account left over is the teacher's to remove).
        var accounts = Shell(Short, "dumpsys", "account").Text;
        if (BlockingAccounts(accounts))
        {
            var others = MiSignOut.AccountTypes(accounts).Where(t => !IsLocalAccount(t)).ToList();
            if (!allowMiAccounts || others.Count == 0 || !others.All(MiSignOut.IsXiaomi))
            {
                return CableProblem.HasAccounts;
            }
            MiAccountsLeft = true;
        }
        if (LocalAuthenticators(accounts) is not { } local)
        {
            return CableProblem.HasAccounts;
        }
        _localAuthenticators = local;

        var users = Shell(Short, "pm", "list", "users").Text;
        if (users.Split("UserInfo{").Length - 1 > 1)
        {
            return CableProblem.HasUsers;
        }

        if (AlreadyOwner())
        {
            return CableProblem.AlreadySchool;
        }

        // A screen lock set before the Midview: Android 10 then turns off for
        // good the reset token the teacher's PIN needs ("Escrow token is
        // disabled", seen on the school's Multilaser M10 tablet). A device
        // that does not say is let through.
        if (SecureKeyguard.IsMatch(Shell(Short, "dumpsys", "window", "policy").Text))
        {
            return CableProblem.ScreenLocked;
        }
        return null;
    }

    // Whether the Midview is the phone's Device Owner already. "dpm
    // list-owners" came with Android 11: Android 10 answers "unknown
    // command", and its owner is read from "dumpsys device_policy"
    // ("Device Owner:" followed by "admin=ComponentInfo{package/...}").
    private bool AlreadyOwner()
    {
        var owners = Shell(Short, "dpm", "list-owners").Text;
        if (!owners.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
        {
            return owners.Contains(PhoneProvisioning.AgentPackage, StringComparison.Ordinal);
        }
        return Regex.IsMatch(
            Shell(Short, "dumpsys", "device_policy").Text,
            @"Device Owner:\s*\n\s*admin=ComponentInfo\{" + Regex.Escape(PhoneProvisioning.AgentPackage) + "/");
    }

    // optimize: "Midview otimizado" arranges a Xiaomi's home screen
    // (ArrangeHome) and lightens the phone (Lighten); "Só o Midview" leaves
    // the rest of it as it came. kind names the device
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
        var payload = CableSetupPayload.Encode(number, masterThumbprint, ssid, password, kind, optimize);
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

        // Before the Mi sign-out: the taps need "Depuração USB (configurações
        // de segurança)", which MIUI may turn off once the account leaves.
        var homeByHand = false;
        if (optimize && Miui)
        {
            stage(CableStage.HomeScreen);
            homeByHand = !ArrangeHome();
        }

        if (signOut)
        {
            stage(CableStage.MiSignOut);
            // Only the accounts are looked at after it: the switches are no
            // longer needed. A failure leaves the app installed and the
            // phone not the school's; "Tentar de novo" starts over.
            if (!MakeSignOut(command => Shell(Short, command)).Run(miPassword!)
                || BlockingAccounts(Shell(Short, "dumpsys", "account").Text))
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

        var owner = WithLocalAccountsHidden(
            () => Shell(Short, "dpm", "set-device-owner", PhoneProvisioning.AdminComponent));
        // From here on nothing else touches the cable.
        if (owner.Text.Contains("Success", StringComparison.Ordinal))
        {
            return new CableResult(phone, null, false, homeByHand);
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
            return new CableResult(phone, null, true, homeByHand);
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

    // Another adb on this PC (Android Studio, scrcpy, another Midview) holds
    // the phone: the Midview's own adb server (AdbProcess) then sees nothing.
    public const string OtherAdbHint =
        "Feche outros programas que usam o celular pelo cabo (Android Studio, scrcpy, outro Midview) e tente de novo.";

    // What the cable step says while no phone is seen; with another adb
    // running on this PC (AdbProcess.OthersRunning), how to free the phone.
    public static string NoPhoneWait(bool otherAdb) =>
        otherAdb ? "Esperando o celular no cabo...\n" + OtherAdbHint : "Esperando o celular no cabo...";

    public static (string Text, string? BackToStep) Describe(CableProblem p) => p switch
    {
        CableProblem.NoPhone => ("O celular não está no cabo.\n" + OtherAdbHint, "cabo"),
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
        CableProblem.ScreenLocked => ("Tire a senha de bloqueio do aparelho antes (Configurações > Segurança > Bloqueio de tela > Nenhum): com ela, o PIN do professor não funciona neste aparelho.", "cabo"),
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };
}
