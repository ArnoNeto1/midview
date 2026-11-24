using System.Text;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// The phones' admin password among the school's defaults of "Preparar
// celulares": kept only as its salted hash, put on each prepared device and
// on each one "Otimizar" reaches.
public sealed class PhoneDefaultsAdminTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-defaults-admin-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

    private static byte[] Payload(string password) => PhoneAdminPassword.Encode(password, new byte[PhoneAdminPassword.SaltLength], 10_000);

    [Fact]
    public void The_admin_password_round_trips_as_a_hash_alone()
    {
        PhoneDefaultsFile.SavePin("2468", _folder);
        PhoneDefaultsFile.SaveAdminPassword("Escola2026", _folder);

        var defaults = PhoneDefaultsFile.Load(_folder);

        Assert.Equal("2468", defaults.Pin);
        Assert.False(defaults.IsEmpty);
        var stored = PhoneAdminPassword.Decode(defaults.AdminPassword!);
        Assert.NotNull(stored);
        Assert.True(PhoneAdminPassword.Verify("Escola2026", stored!));
        Assert.False(PhoneAdminPassword.Verify("Escola2027", stored!));
        Assert.Equal(PhoneAdminPassword.DefaultIterations, stored!.Iterations);
        // Neither the password nor its hash in plain text on disk.
        foreach (var file in Directory.GetFiles(_folder))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.DoesNotContain("Escola2026", Encoding.UTF8.GetString(bytes));
            Assert.DoesNotContain("Escola2026", Encoding.Unicode.GetString(bytes));
            Assert.DoesNotContain(Convert.ToBase64String(defaults.AdminPassword!), Encoding.UTF8.GetString(bytes));
        }
    }

    [Fact]
    public void Saving_the_others_keeps_the_admin_password_and_none_removes_it()
    {
        PhoneDefaultsFile.SaveAdminPassword("abc123", _folder);
        var first = PhoneDefaultsFile.Load(_folder).AdminPassword;

        PhoneDefaultsFile.SavePin("1357", _folder);
        PhoneDefaultsFile.SaveWallpaper(AndroidDeviceKind.Phone, new(PhoneWallpaperScreens.Both, Jpeg), _folder);
        Assert.Equal(first, PhoneDefaultsFile.Load(_folder).AdminPassword);

        PhoneDefaultsFile.SavePin(null, _folder);
        PhoneDefaultsFile.SaveWallpaper(AndroidDeviceKind.Phone, null, _folder);
        Assert.False(PhoneDefaultsFile.Load(_folder).IsEmpty);

        PhoneDefaultsFile.SaveAdminPassword("", _folder);
        Assert.Null(PhoneDefaultsFile.Load(_folder).AdminPassword);
        Assert.True(PhoneDefaultsFile.Load(_folder).IsEmpty);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    // A fresh salt each time it is saved.
    [Fact]
    public void Saving_again_makes_a_new_hash()
    {
        PhoneDefaultsFile.SaveAdminPassword("abc123", _folder);
        var first = PhoneDefaultsFile.Load(_folder).AdminPassword!;
        PhoneDefaultsFile.SaveAdminPassword("abc123", _folder);
        Assert.NotEqual(first, PhoneDefaultsFile.Load(_folder).AdminPassword!);
    }

    [Theory]
    [InlineData("abc12")]
    [InlineData("abc 123")]
    [InlineData("senha-123")]
    [InlineData("1234567890123")]
    public void An_admin_password_out_of_the_rules_is_refused(string password)
    {
        Assert.Throws<ArgumentException>(() => PhoneDefaultsFile.SaveAdminPassword(password, _folder));
        Assert.True(PhoneDefaultsFile.Load(_folder).IsEmpty);
    }

    [Fact]
    public void A_prepared_device_gets_the_admin_password_after_the_pin()
    {
        var payload = Payload("abc123");
        var defaults = new PhoneDefaultsFile.Defaults("2468", new(PhoneWallpaperScreens.Both, Jpeg), null, payload);
        var sent = new List<string>();
        byte[]? got = null;

        var said = PreparedDefaults.Apply("Celular 07", AndroidDeviceKind.Phone, defaults,
            _ => { sent.Add("wallpaper"); return (true, "ok"); },
            _ => { sent.Add("pin"); return (true, "ok"); },
            admin => { sent.Add("admin"); got = admin; return (true, "ok"); });

        Assert.Equal(new[] { "wallpaper", "pin", "admin" }, sent);
        Assert.Equal(payload, got);
        Assert.Equal("Celular 07: papel de parede, PIN e senha de administrador da escola postos.", said);
    }

    [Fact]
    public void The_admin_password_alone_and_its_failure_are_said()
    {
        var onlyAdmin = new PhoneDefaultsFile.Defaults(null, null, null, Payload("abc123"));

        Assert.Equal("Tablet 03: senha de administrador da escola posta.",
            PreparedDefaults.Apply("Tablet 03", AndroidDeviceKind.Tablet, onlyAdmin, _ => (true, ""), _ => (true, ""), _ => (true, "")));
        Assert.Equal("Tablet 03: não deu para pôr a senha de administrador (o aparelho não respondeu).",
            PreparedDefaults.Apply("Tablet 03", AndroidDeviceKind.Tablet, onlyAdmin, _ => (true, ""), _ => (true, ""),
                _ => throw new IOException()));
        Assert.Equal("Tablet 03: não deu para pôr a senha de administrador (o Midview do aparelho é antigo).",
            PreparedDefaults.Apply("Tablet 03", AndroidDeviceKind.Tablet, onlyAdmin, _ => (true, ""), _ => (true, ""), _ => null));

        var pinAndAdmin = onlyAdmin with { Pin = "2468" };
        Assert.Equal("Tablet 03: PIN da escola posto; não deu para pôr a senha de administrador (o celular recusou).",
            PreparedDefaults.Apply("Tablet 03", AndroidDeviceKind.Tablet, pinAndAdmin, _ => (true, ""), _ => (true, ""),
                _ => (false, "o celular recusou")));
    }

    [Fact]
    public void The_boxes_are_checked_with_the_network_windows_rules()
    {
        Assert.Null(PhoneDefaultsText.AdminProblem("", ""));
        Assert.Null(PhoneDefaultsText.AdminProblem("abc123", "abc123"));
        Assert.Equal(PhoneDefaultsText.AdminInvalid, PhoneDefaultsText.AdminProblem("abc12", "abc12"));
        Assert.Equal(PhoneDefaultsText.AdminInvalid, PhoneDefaultsText.AdminProblem("", "abc123"));
        Assert.Equal(PhoneDefaultsText.AdminMismatch, PhoneDefaultsText.AdminProblem("abc123", "abc124"));
        Assert.Equal(PhoneDefaultsText.AdminMismatch, PhoneDefaultsText.AdminProblem("abc123", ""));
    }

    [Fact]
    public void The_hint_follows_the_typing_and_what_is_saved()
    {
        Assert.Equal(PhoneDefaultsText.AdminHint, PhoneDefaultsText.AdminTyping("", saved: false, removed: false));
        Assert.Equal(PhoneDefaultsText.AdminSavedHint, PhoneDefaultsText.AdminTyping("", saved: true, removed: false));
        Assert.Equal(PhoneDefaultsText.AdminRemovedHint, PhoneDefaultsText.AdminTyping("", saved: true, removed: true));
        Assert.Equal("Faltam 3 letras ou números.", PhoneDefaultsText.AdminTyping("abc", saved: false, removed: false));
        Assert.Equal("Senha completa.", PhoneDefaultsText.AdminTyping("abc123", saved: true, removed: false));
    }

    [Fact]
    public void The_card_says_what_the_password_frees()
    {
        Assert.Equal("Senha de administrador dos celulares e tablets", PhoneDefaultsText.AdminTitle);
        Assert.Equal(
            "Para liberar no próprio aparelho, sem internet, o Wi-Fi, a depuração USB e as contas, ou tirar o Midview. " +
            "Pelo menos 6 letras ou números.",
            PhoneDefaultsText.AdminLine);
        Assert.Equal("Repita a senha", PhoneDefaultsText.AdminRepeat);
        Assert.Equal("a", PhoneDefaultsText.List(new[] { "a" }));
        Assert.Equal("a e b", PhoneDefaultsText.List(new[] { "a", "b" }));
        Assert.Equal("a, b e c", PhoneDefaultsText.List(new[] { "a", "b", "c" }));
    }

    [Fact]
    public void Otimizar_sends_the_saved_admin_password_after_optimizing()
    {
        var report = new PhoneOptimizeReport(null, Array.Empty<string>(), Array.Empty<string>(), PhoneDnsStates.On, "");
        var payload = Payload("abc123");
        byte[]? got = null;

        Assert.Equal(PhoneOptimizeAdmin.Put, PhoneOptimizeAdmin.Send(report, payload, admin => { got = admin; return (true, "ok"); }));
        Assert.Equal(payload, got);
        Assert.Equal("senha de administrador não posta (o celular recusou)",
            PhoneOptimizeAdmin.Send(report, payload, _ => (false, "o celular recusou")));
        Assert.Equal("senha de administrador não posta (o aparelho não respondeu)",
            PhoneOptimizeAdmin.Send(report, payload, _ => throw new TimeoutException()));
        Assert.Equal("1 app escondido · senha de administrador posta", PhoneOptimizeAdmin.Line("1 app escondido", PhoneOptimizeAdmin.Put));
        Assert.Equal("1 app escondido", PhoneOptimizeAdmin.Line("1 app escondido", null));
    }

    [Fact]
    public void Otimizar_sends_nothing_without_a_saved_password_or_to_an_old_agent()
    {
        var report = new PhoneOptimizeReport(null, Array.Empty<string>(), Array.Empty<string>(), PhoneDnsStates.On, "");
        Assert.Null(PhoneOptimizeAdmin.Send(report, null, _ => throw new InvalidOperationException("not sent")));
        Assert.Null(PhoneOptimizeAdmin.Send(null, Payload("abc123"), _ => throw new InvalidOperationException("not sent")));
    }

    // The page keeps nothing of the password anywhere but the defaults'
    // DPAPI blob (PhoneDefaultsFile), and never logs it.
    [Fact]
    public void The_page_writes_the_password_nowhere_else()
    {
        var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Midview.Master", "PreparePhonesWindow.AdminPassword.cs"))
            .Replace("PhoneDefaultsFile.", "");
        Assert.DoesNotContain("File.", code);
        Assert.DoesNotContain("MasterData", code);
        Assert.DoesNotContain("Registry", code);
        Assert.DoesNotContain("Log", code);
        Assert.DoesNotContain("Trace", code);
    }

    [Fact]
    public void Otimizar_asks_for_both_boxes_with_the_same_valid_password()
    {
        Assert.Equal(PhoneDefaultsText.AskEmpty, PhoneDefaultsText.AskProblem("", ""));
        Assert.Equal(PhoneDefaultsText.AskInvalid, PhoneDefaultsText.AskProblem("abc", "abc"));
        Assert.Equal(PhoneDefaultsText.ChangeEmpty, PhoneDefaultsText.AskProblem("", "", change: true));
        Assert.Equal(PhoneDefaultsText.AdminMismatch, PhoneDefaultsText.AskProblem("Escola2026", "Escola2027"));
        Assert.Equal(PhoneDefaultsText.AdminMismatch, PhoneDefaultsText.AskProblem("Escola2026", ""));
        Assert.Null(PhoneDefaultsText.AskProblem("Escola2026", "Escola2026"));
    }

    [Fact]
    public void Otimizar_asks_only_when_no_admin_password_is_saved_and_never_keeps_the_typed_one()
    {
        var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Midview.Master", "PhoneOptimizeWindow.xaml.cs"));
        Assert.Contains("PhoneDefaultsFile.Load().AdminPassword is not null", code);
        Assert.Contains("new OptimizeAdminWindow", code);
        var ask = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Midview.Master", "OptimizeAdminWindow.xaml.cs"))
            .Replace("PhoneDefaultsFile.", "");
        Assert.DoesNotContain("File.", ask);
        Assert.DoesNotContain("MasterData", ask);
        Assert.DoesNotContain("Log", ask);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Midview.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
