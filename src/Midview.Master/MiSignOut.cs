using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace Midview.Master;

// One element of a uiautomator dump: what it says, whose it is and where its
// middle is on the screen.
internal sealed record UiNode(
    string Text, string Description, string Id, string Class, string Package,
    bool Password, bool Scrollable, int X, int Y, int Top, int Bottom);

// Signs a Xiaomi phone out of the school's Mi account over the cable, the way
// the teacher would on its screen: the Mi account settings, "Sair", the
// account's password when asked, "Manter" when asked whether to delete the
// synced items (a phone being prepared has none, and keeping them only signs
// out). Only with "Depuração USB (configurações de segurança)" on, which lets
// the cable tap and type; the school's account is what turned it on.
// Inspected read-only on the school's Redmi 10C (MIUI 14, Android 13,
// com.xiaomi.account R-23.12.01.01): the action below opens
// com.xiaomi.account/.ui.AccountSettingsActivity, whose button is
// id/logout_btn or id/btn_logout, "Sair" ("Sign out"); the synced items
// question is com.miui.cloudservice's "Remover"/"Manter". The signed-in
// screens themselves were never seen live: every step looks at the screen
// first, and anything unexpected ends it with the phone left as it is.
// The password goes only to the phone's "input text"; never to a log, an
// exception or a text the teacher sees.
public sealed class MiSignOut
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    // The Mi account settings ("Conta Mi" at the top of Settings).
    internal const string SettingsAction = "android.settings.XIAOMI_ACCOUNT_SYNC_SETTINGS";

    internal const string DumpFile = "/data/local/tmp/midview-ui.xml";

    // After typing, how long the password prompt may stay while the phone
    // checks it online; still there after this: a wrong password.
    private static readonly TimeSpan PromptGrace = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private static readonly Regex AccountType = new(@"Account \{name=.*?, type=([\w.]+)\}", RegexOptions.Compiled);

    private static readonly string[] SignOutTexts = { "Sair", "Sair da conta", "Fazer logout", "Sign out", "Log out" };
    private static readonly string[] SignOutIds = { "/logout_btn", "/btn_logout" };
    private static readonly string[] KeepTexts = { "Manter", "Keep", "Manter no dispositivo", "Manter dados" };
    private static readonly string[] ConfirmTexts =
        { "OK", "Confirmar", "Avançar", "Próximo", "Continuar", "Sair", "Desativar", "Confirm", "Next", "Sign out", "Turn off" };

    private readonly Func<string[], AdbOutput> _shell;
    private readonly Action<TimeSpan> _pause;
    private readonly Func<TimeSpan> _elapsed;

    // shell: one "adb shell" command on the phone. pause and elapsed: the
    // clock (a fake one in the tests).
    public MiSignOut(Func<string[], AdbOutput> shell, Action<TimeSpan> pause, Func<TimeSpan> elapsed)
    {
        _shell = shell;
        _pause = pause;
        _elapsed = elapsed;
    }

    public static MiSignOut WithRealClock(Func<string[], AdbOutput> shell)
    {
        var watch = Stopwatch.StartNew();
        return new MiSignOut(shell, Thread.Sleep, () => watch.Elapsed);
    }

    // The account types "dumpsys account" lists ("com.xiaomi", "com.google").
    internal static IReadOnlyList<string> AccountTypes(string dumpsys) =>
        AccountType.Matches(dumpsys).Select(m => m.Groups[1].Value).ToList();

    internal static bool IsXiaomi(string type) => type == "com.xiaomi" || type.StartsWith("com.xiaomi.", StringComparison.Ordinal);

    // Every account on the phone is a Mi account: only then is the sign-out
    // tried (a Google account left over is the teacher's to remove).
    public static bool OnlyXiaomi(string dumpsys)
    {
        var types = AccountTypes(dumpsys);
        return types.Count > 0 && types.All(IsXiaomi);
    }

    // The password as one word for "input text" on the phone's shell, or null
    // when it cannot be typed safely: "input text" types only plain ASCII,
    // and turns "%s" into a space (how a space is typed), so a password with
    // an accent or with "%s" in it is left to the teacher. Single quotes keep
    // the phone's shell from reading anything in it.
    internal static string? Typed(string password)
    {
        if (password.Length == 0 || password.Contains("%s", StringComparison.Ordinal)
            || password.Any(c => c < ' ' || c > '~'))
        {
            return null;
        }
        return "'" + password.Replace(" ", "%s", StringComparison.Ordinal).Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    // True once no Mi account is left on the phone. False on any failure,
    // with the phone left as it is (back on its home screen).
    public bool Run(string password)
    {
        if (Typed(password) is not { } typed)
        {
            return false;
        }
        try
        {
            return Drive(typed);
        }
        finally
        {
            Shell("input", "keyevent", "KEYCODE_HOME");
            Shell("rm", "-f", DumpFile);
        }
    }

    private AdbOutput Shell(params string[] command) => _shell(command);

    private bool Drive(string typed)
    {
        Shell("input", "keyevent", "KEYCODE_WAKEUP");
        Shell("wm", "dismiss-keyguard");
        Shell("am", "start", "-W", "-a", SettingsAction);
        _pause(Settle);

        var signOutTaps = 0;
        var keepTaps = 0;
        var swipes = 0;
        TimeSpan? typedAt = null;
        var confirmed = false;
        while (_elapsed() < Limit)
        {
            if (SignedOut())
            {
                return true;
            }
            if (Screen() is not { } nodes)
            {
                _pause(Second);
                continue;
            }

            if (PasswordField(nodes) is { } field)
            {
                if (typedAt is null)
                {
                    Tap(field);
                    Shell("input", "text", typed);
                    typedAt = _elapsed();
                    _pause(Second);
                    continue;
                }
                if (!confirmed)
                {
                    // Looked for after typing: the keyboard moves the dialog.
                    // The dialog's own positive button first: the screen under it may say "Sair" too.
                    if ((nodes.FirstOrDefault(n => Ours(n) && n.Id == "android:id/button1") ?? Find(nodes, ConfirmTexts)) is not { } ok)
                    {
                        return false;
                    }
                    Tap(ok);
                    confirmed = true;
                    _pause(Settle);
                    continue;
                }
                // Never typed twice: a wrong password stays, and a second try
                // could lock the school's account.
                if (_elapsed() - typedAt > PromptGrace)
                {
                    return false;
                }
                _pause(Second);
                continue;
            }

            if (keepTaps < 2 && Find(nodes, KeepTexts) is { } keep)
            {
                Tap(keep);
                keepTaps++;
                _pause(Settle);
                continue;
            }
            if (Find(nodes, SignOutTexts, SignOutIds) is { } signOut)
            {
                if (signOutTaps >= 4)
                {
                    return false;
                }
                Tap(signOut);
                signOutTaps++;
                _pause(Settle);
                continue;
            }
            // "Sair" is at the foot of the settings list: scrolled to, only
            // before it was ever tapped.
            if (signOutTaps == 0 && swipes < 4 && nodes.FirstOrDefault(n => n.Scrollable && Ours(n)) is { } list)
            {
                var x = list.X.ToString(CultureInfo.InvariantCulture);
                var from = (list.Top + (list.Bottom - list.Top) * 4 / 5).ToString(CultureInfo.InvariantCulture);
                var to = (list.Top + (list.Bottom - list.Top) / 5).ToString(CultureInfo.InvariantCulture);
                Shell("input", "swipe", x, from, x, to, "300");
                swipes++;
                _pause(Second);
                continue;
            }
            _pause(Second);
        }
        return SignedOut();
    }

    // Only a sure "no Mi account": an answer without the accounts list (the
    // cable dropped) is not one.
    private bool SignedOut()
    {
        var text = Shell("dumpsys", "account").Text;
        return text.Contains("Accounts:", StringComparison.Ordinal) && !AccountTypes(text).Any(IsXiaomi);
    }

    private List<UiNode>? Screen()
    {
        Shell("uiautomator", "dump", DumpFile);
        return Parse(Shell("cat", DumpFile).Text);
    }

    private void Tap(UiNode node) => Shell(
        "input", "tap", node.X.ToString(CultureInfo.InvariantCulture), node.Y.ToString(CultureInfo.InvariantCulture));

    // Taps only ever land on Xiaomi's own screens and dialogs.
    private static bool Ours(UiNode node) =>
        node.Package.StartsWith("com.xiaomi.", StringComparison.Ordinal) || node.Package.StartsWith("com.miui.", StringComparison.Ordinal);

    // The password prompt. Not the sign-in screen (it has the account
    // field too), where the password must never be typed.
    private static UiNode? PasswordField(List<UiNode> nodes)
    {
        if (nodes.Any(n => n.Id.EndsWith("/et_account_name", StringComparison.Ordinal)))
        {
            return null;
        }
        return nodes.FirstOrDefault(n => Ours(n) && n.Password && n.Class.EndsWith("EditText", StringComparison.Ordinal));
    }

    private static UiNode? Find(List<UiNode> nodes, string[] texts, string[]? ids = null) =>
        nodes.FirstOrDefault(n => Ours(n)
            && (texts.Any(t => string.Equals(n.Text.Trim(), t, StringComparison.OrdinalIgnoreCase))
                || (ids ?? Array.Empty<string>()).Any(id => n.Id == id || n.Id.EndsWith(id, StringComparison.Ordinal))));

    // A dump's nodes, or null when the text holds none (the dump failed:
    // "could not get idle state", the cable dropped).
    internal static List<UiNode>? Parse(string text)
    {
        var start = text.IndexOf("<hierarchy", StringComparison.Ordinal);
        var end = text.LastIndexOf("</hierarchy>", StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            return null;
        }
        XElement root;
        try
        {
            root = XElement.Parse(text[start..(end + "</hierarchy>".Length)]);
        }
        catch (XmlException)
        {
            return null;
        }
        var nodes = new List<UiNode>();
        foreach (var node in root.Descendants("node"))
        {
            var bounds = Regex.Match((string?)node.Attribute("bounds") ?? "", @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
            if (!bounds.Success)
            {
                continue;
            }
            int At(int i) => int.Parse(bounds.Groups[i].Value, CultureInfo.InvariantCulture);
            nodes.Add(new UiNode(
                (string?)node.Attribute("text") ?? "",
                (string?)node.Attribute("content-desc") ?? "",
                (string?)node.Attribute("resource-id") ?? "",
                (string?)node.Attribute("class") ?? "",
                (string?)node.Attribute("package") ?? "",
                (string?)node.Attribute("password") == "true",
                (string?)node.Attribute("scrollable") == "true",
                (At(1) + At(3)) / 2, (At(2) + At(4)) / 2, At(2), At(4)));
        }
        return nodes;
    }
}
