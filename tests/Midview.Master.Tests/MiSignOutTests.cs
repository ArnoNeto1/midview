using System;
using System.Collections.Generic;
using System.Linq;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

public sealed class MiSignOutTests
{
    private const string Dump = "cat " + MiSignOut.DumpFile;
    private const string Password = "Escola 2026!";

    private const string XiaomiAccounts =
        "User UserInfo{0:Proprietário:c13}:\n  Accounts: 1\n    Account {name=6512345678, type=com.xiaomi}\n";
    private const string NoAccounts = "User UserInfo{0:Proprietário:c13}:\n  Accounts: 0\n";

    // One element of a scripted screen.
    private static string Node(
        string text, string bounds, string id = "", string package = "com.xiaomi.account",
        string cls = "android.widget.Button", bool password = false, bool scrollable = false) =>
        $"<node index=\"0\" text=\"{text}\" resource-id=\"{id}\" class=\"{cls}\" package=\"{package}\" content-desc=\"\" " +
        $"clickable=\"true\" enabled=\"true\" scrollable=\"{(scrollable ? "true" : "false")}\" " +
        $"password=\"{(password ? "true" : "false")}\" bounds=\"{bounds}\" />";

    // A dump as the phone prints it (MIUI adds a stack trace before it).
    private static string Screen(params string[] nodes) =>
        "java.io.FileNotFoundException: /data/system/theme_config/theme_compatibility.xml\n" +
        "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?><hierarchy rotation=\"0\">" +
        "<node index=\"0\" text=\"\" resource-id=\"\" class=\"android.widget.FrameLayout\" package=\"com.xiaomi.account\" " +
        "content-desc=\"\" scrollable=\"false\" password=\"false\" bounds=\"[0,0][720,1600]\">" +
        string.Concat(nodes) + "</node></hierarchy>";

    // The Mi account settings, "Sair" at the foot (tap at 360,1340).
    private static readonly string Settings = Screen(
        Node("Xiaomi Cloud", "[0,400][720,500]", cls: "android.widget.TextView"),
        Node("Sair", "[60,1300][660,1380]", "com.xiaomi.account:id/logout_btn"));

    // The password prompt: field at 360,650, OK at 530,840.
    private static readonly string PasswordPrompt = Screen(
        Node("Inserir senha", "[60,600][660,700]", "com.xiaomi.account:id/input_pwd_view", cls: "android.widget.EditText", password: true),
        Node("Cancelar", "[60,800][320,880]", "android:id/button2"),
        Node("OK", "[400,800][660,880]", "android:id/button1"));

    // "Excluir itens sincronizados deste dispositivo?": Remover at 190,1440, Manter at 530,1440.
    private static readonly string KeepOrRemove = Screen(
        Node("Excluir itens sincronizados deste dispositivo?", "[40,1200][680,1260]", package: "com.miui.cloudservice", cls: "android.widget.TextView"),
        Node("Remover", "[60,1400][320,1480]", "android:id/button2", "com.miui.cloudservice"),
        Node("Manter", "[400,1400][660,1480]", "android:id/button1", "com.miui.cloudservice"));

    // A phone driven by the cable: what is on its screen, and what a tap there does.
    private sealed class FakePhone
    {
        public readonly List<string> Calls = new();
        public string Showing = Settings;
        public bool HasMi = true;
        public readonly Dictionary<(int X, int Y), Action> OnTap = new();

        public TimeSpan Elapsed;

        public AdbOutput Shell(string[] command)
        {
            var key = string.Join(' ', command);
            Calls.Add(key);
            if (key == "dumpsys account")
            {
                return new AdbOutput(0, HasMi ? XiaomiAccounts : NoAccounts, false);
            }
            if (key == Dump)
            {
                return new AdbOutput(0, Showing, false);
            }
            if (command.Length == 4 && command[0] == "input" && command[1] == "tap"
                && OnTap.TryGetValue((int.Parse(command[2]), int.Parse(command[3])), out var tap))
            {
                tap();
            }
            return new AdbOutput(0, "", false);
        }

        public MiSignOut SignOut() => new(Shell, pause => Elapsed += pause, () => Elapsed);

        public int Count(string prefix) => Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));
    }

    // Sair, then the password, then Manter: signed out.
    private static FakePhone Signing()
    {
        var phone = new FakePhone();
        phone.OnTap[(360, 1340)] = () => phone.Showing = PasswordPrompt;
        phone.OnTap[(530, 840)] = () => phone.Showing = KeepOrRemove;
        phone.OnTap[(530, 1440)] = () =>
        {
            phone.HasMi = false;
            phone.Showing = Screen();
        };
        return phone;
    }

    [Fact]
    public void Signs_out_with_the_password_and_keeps_the_items()
    {
        var phone = Signing();
        Assert.True(phone.SignOut().Run(Password));

        Assert.Contains("am start -W -a android.settings.XIAOMI_ACCOUNT_SYNC_SETTINGS", phone.Calls);
        Assert.Equal(new[] { "input text 'Escola%s2026!'" }, phone.Calls.Where(c => c.StartsWith("input text", StringComparison.Ordinal)));
        // The field is tapped before typing, OK after.
        var typed = phone.Calls.IndexOf("input text 'Escola%s2026!'");
        Assert.Equal("input tap 360 650", phone.Calls[typed - 1]);
        Assert.True(phone.Calls.IndexOf("input tap 530 840") > typed);
        Assert.Contains("input tap 530 1440", phone.Calls);
        Assert.DoesNotContain("input tap 190 1440", phone.Calls);
        // Back home, the dump removed.
        Assert.Equal(new[] { "input keyevent KEYCODE_HOME", "rm -f " + MiSignOut.DumpFile }, phone.Calls.TakeLast(2));
    }

    [Fact]
    public void Without_a_sign_out_button_it_gives_up_in_time_and_types_nothing()
    {
        var phone = new FakePhone
        {
            Showing = Screen(
                Node("", "[0,200][720,1500]", "com.xiaomi.account:id/list", cls: "androidx.recyclerview.widget.RecyclerView", scrollable: true),
                Node("Xiaomi Cloud", "[0,400][720,500]", cls: "android.widget.TextView")),
        };
        Assert.False(phone.SignOut().Run(Password));

        Assert.True(phone.Elapsed >= MiSignOut.Limit);
        Assert.True(phone.Elapsed < MiSignOut.Limit + TimeSpan.FromSeconds(5));
        Assert.Equal(0, phone.Count("input text"));
        Assert.Equal(0, phone.Count("input tap"));
        // The list is scrolled to look for "Sair", a few times only.
        Assert.Equal(4, phone.Count("input swipe 360 1240 360 460"));
        Assert.Equal("input keyevent KEYCODE_HOME", phone.Calls[^2]);
    }

    [Fact]
    public void A_wrong_password_is_never_typed_twice()
    {
        var phone = Signing();
        // OK leaves the prompt up: the phone did not take the password.
        phone.OnTap[(530, 840)] = () => { };
        Assert.False(phone.SignOut().Run(Password));

        Assert.Equal(1, phone.Count("input text"));
        Assert.Equal(1, phone.Calls.Count(c => c == "input tap 530 840"));
        Assert.True(phone.Elapsed < MiSignOut.Limit);
        Assert.Equal("input keyevent KEYCODE_HOME", phone.Calls[^2]);
    }

    [Fact]
    public void An_account_still_there_after_every_step_is_a_failure()
    {
        var phone = Signing();
        // Manter goes back to the settings, the Mi account still in.
        phone.OnTap[(530, 1440)] = () => phone.Showing = Settings;
        phone.OnTap[(530, 840)] = () => phone.Showing = KeepOrRemove;
        var typedOnce = false;
        phone.OnTap[(360, 1340)] = () =>
        {
            phone.Showing = typedOnce ? KeepOrRemove : PasswordPrompt;
            typedOnce = true;
        };
        Assert.False(phone.SignOut().Run(Password));

        Assert.Equal(1, phone.Count("input text"));
        Assert.True(phone.Count("input tap 360 1340") <= 4);
        Assert.Equal("input keyevent KEYCODE_HOME", phone.Calls[^2]);
    }

    [Fact]
    public void A_password_it_cannot_type_touches_nothing()
    {
        var phone = Signing();
        Assert.False(phone.SignOut().Run("senha%secreta"));
        Assert.False(phone.SignOut().Run("ação1234"));
        Assert.Empty(phone.Calls);
    }

    [Fact]
    public void The_sign_in_screen_never_gets_the_password()
    {
        var phone = new FakePhone
        {
            Showing = Screen(
                Node("E-mail/Número de telefone/ID da Conta Xiaomi", "[51,373][669,470]", "com.xiaomi.account:id/et_account_name", cls: "android.widget.EditText"),
                Node("Inserir senha", "[51,494][669,591]", "com.xiaomi.account:id/input_pwd_view", cls: "android.widget.EditText", password: true)),
        };
        Assert.False(phone.SignOut().Run(Password));
        Assert.Equal(0, phone.Count("input text"));
    }

    [Fact]
    public void Taps_only_land_on_xiaomi_screens()
    {
        var phone = new FakePhone { Showing = Screen(Node("Sair", "[60,1300][660,1380]", package: "com.android.chrome")) };
        Assert.False(phone.SignOut().Run(Password));
        Assert.Equal(0, phone.Count("input tap"));
    }

    [Theory]
    [InlineData("Escola2026", "'Escola2026'")]
    [InlineData("Escola 2026", "'Escola%s2026'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("a\"b$c`d\\e;f&g|h", "'a\"b$c`d\\e;f&g|h'")]
    [InlineData("50% off", "'50%%soff'")]
    [InlineData("100%", "'100%'")]
    public void Typed_escapes_for_the_phone_shell(string password, string expected) =>
        Assert.Equal(expected, MiSignOut.Typed(password));

    [Theory]
    [InlineData("")]
    [InlineData("senha%s")]
    [InlineData("ação")]
    [InlineData("tab\there")]
    [InlineData("emoji\U0001F600")]
    public void Typed_refuses_what_input_text_cannot_type(string password) =>
        Assert.Null(MiSignOut.Typed(password));

    [Fact]
    public void Only_xiaomi_accounts_are_signed_out()
    {
        Assert.True(MiSignOut.OnlyXiaomi(XiaomiAccounts));
        Assert.False(MiSignOut.OnlyXiaomi(NoAccounts));
        Assert.False(MiSignOut.OnlyXiaomi(
            "  Accounts: 2\n    Account {name=6512345678, type=com.xiaomi}\n    Account {name=escola@gmail.com, type=com.google}\n"));
        Assert.True(MiSignOut.OnlyXiaomi("  Accounts: 1\n    Account {name=x, y, type=com.xiaomi.unactivated}\n"));
    }

    [Fact]
    public void A_failed_dump_has_no_nodes()
    {
        Assert.Null(MiSignOut.Parse("ERROR: could not get idle state.\n"));
        Assert.Null(MiSignOut.Parse("<hierarchy><node"));
        var nodes = MiSignOut.Parse(Settings)!;
        Assert.Contains(nodes, n => n.Text == "Sair" && n.X == 360 && n.Y == 1340 && n.Id.EndsWith("/logout_btn"));
    }
}
