using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// "Abrir site", "Abrir programa", "Instalar programa" and "Desinstalar
// programa" with phones: the windows' words, who gets what, and what each
// phone's answer becomes.
public class PhoneProgramsTextTests
{
    private static KnownMachine Pc(string name) => new(name, "192.0.2.1", "ANY", "AGENT");

    private static KnownMachine Phone(string name, string? agent = "AB12") =>
        new(name, "192.0.2.9", "ANY", agent, Kind: DeviceKind.Android);

    private static readonly KnownMachine[] Pcs = { Pc("ALUNO-01"), Pc("ALUNO-02") };
    private static readonly KnownMachine[] Phones = { Phone("Celular 01"), Phone("Celular 02") };
    private static readonly KnownMachine[] Mixed = { Pc("ALUNO-01"), Phone("Celular 01"), Phone("Celular 02") };

    [Fact]
    public void An_older_phone_is_named_for_what_it_does_not_do_yet()
    {
        Assert.Equal("o Midview deste celular é de uma versão anterior e ainda não abre sites: atualize-o",
            PhoneProgramsText.OldAgent(MessageType.OpenWebsite));
        Assert.Contains("ainda não abre apps", PhoneProgramsText.OldAgent(MessageType.OpenProgram));
        Assert.Contains("ainda não instala apps", PhoneProgramsText.OldAgent(MessageType.InstallProgram));
        Assert.Contains("ainda não desinstala apps", PhoneProgramsText.OldAgent(MessageType.UninstallPrograms));
    }

    [Fact]
    public void The_site_window_words_phones_alone_and_with_pcs()
    {
        Assert.Equal(PhoneProgramsText.PcWebsiteHint, PhoneProgramsText.WebsiteHint(Pcs));
        Assert.Null(PhoneProgramsText.WebsiteNote(Pcs));
        Assert.Equal("Abrir nas máquinas", PhoneProgramsText.OpenButton(Pcs));
        Assert.Equal("Em 2 máquinas: ALUNO-01, ALUNO-02", PhoneProgramsText.Describe(Pcs, wholeClass: false));

        Assert.Equal(PhoneProgramsText.PhoneWebsiteHint, PhoneProgramsText.WebsiteHint(Phones));
        Assert.Contains("Chrome", PhoneProgramsText.PhoneWebsiteHint);
        Assert.Null(PhoneProgramsText.WebsiteNote(Phones));
        Assert.Equal("Abrir nos celulares", PhoneProgramsText.OpenButton(Phones));
        Assert.Equal("Abrir no celular", PhoneProgramsText.OpenButton(new[] { Phone("Celular 01") }));
        Assert.Equal("Em 2 celulares: Celular 01, Celular 02", PhoneProgramsText.Describe(Phones, wholeClass: false));

        Assert.Equal(PhoneProgramsText.PcWebsiteHint, PhoneProgramsText.WebsiteHint(Mixed));
        Assert.Equal(PhoneProgramsText.PhoneWebsiteNote, PhoneProgramsText.WebsiteNote(Mixed));
        Assert.Equal("Na turma toda (3 aparelhos)", PhoneProgramsText.Describe(Mixed, wholeClass: true));
    }

    [Theory]
    [InlineData(1, 0, "Aberto: x")]
    [InlineData(2, 1, "Aberto em 1 de 2: x")]
    [InlineData(1, 1, "Não abriu: x")]
    [InlineData(3, 3, "Não abriu: x")]
    public void The_summary_counts_what_opened(int total, int failures, string expected) =>
        Assert.Equal(expected, PhoneProgramsText.Opened(Pcs, total, failures, "x"));

    [Fact]
    public void The_summary_names_the_kind_of_machines()
    {
        Assert.Equal("Aberto nas 2 máquinas: x", PhoneProgramsText.Opened(Pcs, 2, 0, "x"));
        Assert.Equal("Aberto nos 2 celulares: x", PhoneProgramsText.Opened(Phones, 2, 0, "x"));
        Assert.Equal("Aberto nos 3 aparelhos: x", PhoneProgramsText.Opened(Mixed, 3, 0, "x"));
    }

    [Fact]
    public void The_pcs_windows_point_to_the_phones_ones()
    {
        Assert.Equal("Escolher o app do celular...", PhoneProgramsText.PhonesButton(1));
        Assert.Equal("Escolher o app dos celulares...", PhoneProgramsText.PhonesButton(3));
        Assert.Contains("não um comando", PhoneProgramsText.PhonesNote(3));
        Assert.Null(PhoneProgramsText.SavedCommandLeftOut(0));
        Assert.Contains("O celular ficou de fora", PhoneProgramsText.SavedCommandLeftOut(1));
        Assert.Contains("Os 2 celulares ficaram de fora", PhoneProgramsText.SavedCommandLeftOut(2));
        Assert.Equal("Desinstalar apps dos celulares...", PhoneProgramsText.UninstallPhonesButton(2));
        Assert.Equal("Abrir app nos celulares", PhoneProgramsText.OpenAppHeading(2));
        Assert.Equal("Desinstalar apps do celular", PhoneProgramsText.UninstallHeading(1));
    }

    [Fact]
    public void A_tiles_menu_says_app_for_phones_alone()
    {
        Assert.Equal("Abrir app", PhoneProgramsText.MenuItem(ToolbarAction.OpenProgram, Phones));
        Assert.Equal("Instalar app...", PhoneProgramsText.MenuItem(ToolbarAction.Install, Phones));
        Assert.Equal("Desinstalar app...", PhoneProgramsText.MenuItem(ToolbarAction.Uninstall, Phones));
        Assert.Equal("Abrir programa", PhoneProgramsText.MenuItem(ToolbarAction.OpenProgram, Mixed));
        Assert.Equal("Desinstalar programa...", PhoneProgramsText.MenuItem(ToolbarAction.Uninstall, Pcs));
        Assert.Null(PhoneProgramsText.MenuItem(ToolbarAction.OpenWebsite, Phones));
    }

    private static PhoneApp App(string package, string label, PhoneAppFlags flags = PhoneAppFlags.None) =>
        new(package, label, Array.Empty<byte>(), flags);

    [Fact]
    public void The_joined_list_says_where_an_app_is_and_whether_it_is_blocked()
    {
        var answers = new IReadOnlyList<PhoneApp>?[]
        {
            new[] { App("com.miui.calculator", "Calculadora", PhoneAppFlags.System | PhoneAppFlags.Blocked), App("com.x.ola", "Olá", PhoneAppFlags.InstalledByMidview) },
            new[] { App("com.miui.calculator", "Calculadora", PhoneAppFlags.System) },
            null,
        };

        var open = PhoneProgramsText.Picks(answers, forUninstall: false);
        Assert.Equal(new[] { "Calculadora", "Olá" }, open.Select(pick => pick.Name));
        Assert.Equal("bloqueado em 1", open[0].Detail);
        Assert.Equal("em 1 de 2 celulares", open[1].Detail);
        Assert.All(open, pick => Assert.True(pick.CanPick));

        var remove = PhoneProgramsText.Picks(answers, forUninstall: true);
        Assert.Equal(new PhoneAppPick("com.miui.calculator", "Calculadora", "veio com o celular: não sai", CanPick: false), remove[0]);
        Assert.Equal(new PhoneAppPick("com.x.ola", "Olá", "em 1 de 2 celulares", CanPick: true), remove[1]);
    }

    // The store's, the maker's: only what the Midview installed is offered.
    [Fact]
    public void Only_what_the_midview_installed_can_be_uninstalled()
    {
        var answers = new IReadOnlyList<PhoneApp>?[]
        {
            new[] { App("com.zhiliaoapp.musically", "TikTok"), App("com.x.ola", "Olá", PhoneAppFlags.InstalledByMidview) },
        };
        var remove = PhoneProgramsText.Picks(answers, forUninstall: true);
        Assert.Equal(new PhoneAppPick("com.zhiliaoapp.musically", "TikTok", "não foi instalado pelo Midview", CanPick: false), remove[1]);
        Assert.True(remove[0].CanPick);
        Assert.All(PhoneProgramsText.Picks(answers, forUninstall: false), pick => Assert.True(pick.CanPick));
    }

    [Fact]
    public void An_app_blocked_on_every_phone_that_has_it_is_just_blocked()
    {
        var answers = new IReadOnlyList<PhoneApp>?[] { new[] { App("com.x.ola", "Olá", PhoneAppFlags.Blocked) } };
        Assert.Equal("bloqueado", PhoneProgramsText.Picks(answers, forUninstall: false).Single().Detail);
    }

    // Without the flags nothing is known to be the Midview's: nothing is offered.
    [Fact]
    public void A_phone_from_before_the_flags_offers_nothing_to_uninstall() =>
        Assert.All(
            PhoneProgramsText.Picks(new IReadOnlyList<PhoneApp>?[] { new[] { App("com.miui.calculator", "Calculadora") } }, forUninstall: true),
            pick => Assert.False(pick.CanPick));

    [Fact]
    public void An_apk_goes_to_the_phones_and_an_installer_to_the_pcs()
    {
        Assert.Equal(new[] { "Celular 01", "Celular 02" }, PhoneProgramsText.InstallTargets(Mixed, "ola.apk").Select(machine => machine.Name));
        Assert.Equal(new[] { "ALUNO-01" }, PhoneProgramsText.InstallTargets(Mixed, "setup.exe").Select(machine => machine.Name));
        Assert.Equal("Um .apk instala só nos celulares: 1 computador fica de fora.", PhoneProgramsText.InstallLeftOut(Mixed, "ola.apk"));
        Assert.Equal("Os celulares instalam só apps .apk: 2 celulares ficam de fora.", PhoneProgramsText.InstallLeftOut(Mixed, "setup.msi"));
        Assert.Null(PhoneProgramsText.InstallLeftOut(Phones, "ola.apk"));
        Assert.Null(PhoneProgramsText.InstallLeftOut(Pcs, "setup.exe"));
    }

    [Fact]
    public void The_install_window_offers_the_files_each_kind_takes()
    {
        Assert.Equal("Aplicativos de Android (*.apk)|*.apk", PhoneProgramsText.FileFilter(Phones));
        Assert.EndsWith("|*.exe;*.msi;*.bat;*.cmd;*.apk", PhoneProgramsText.FileFilter(Mixed));
        Assert.EndsWith("|*.exe;*.msi;*.bat;*.cmd", PhoneProgramsText.FileFilter(Pcs));
        Assert.Equal(PhoneProgramsText.PhoneHint, PhoneProgramsText.InstallHint(Phones));
        Assert.Equal(PhoneProgramsText.PcHint, PhoneProgramsText.InstallHint(Mixed));
        Assert.Equal("Escolher o app (.apk)...", PhoneProgramsText.ChooseButton(Phones));
        Assert.Equal("Nenhum app escolhido (.apk).", PhoneProgramsText.NothingChosen(Phones));
    }

    [Fact]
    public void A_file_the_machines_cannot_take_is_said()
    {
        Assert.Null(PhoneProgramsText.FileProblem("ola.apk", 1000, Phones));
        Assert.Null(PhoneProgramsText.FileProblem("ola.apk", 1000, Mixed));
        Assert.Null(PhoneProgramsText.FileProblem("setup.exe", 1000, Mixed));
        Assert.Equal("ola.apk passa de 200 MB e não pode ser enviado aos celulares.",
            PhoneProgramsText.FileProblem("ola.apk", ProgramInstall.MaxApkSize + 1, Phones));
        Assert.Equal("ola.apk é um app de Android: os computadores não o instalam.", PhoneProgramsText.FileProblem("ola.apk", 1000, Pcs));
        Assert.Equal("setup.exe é um instalador de computador: os celulares instalam só apps de Android (.apk).",
            PhoneProgramsText.FileProblem("setup.exe", 1000, Phones));
        Assert.Equal("foto.png não é um app de Android (.apk).", PhoneProgramsText.FileProblem("foto.png", 1000, Phones));
        Assert.Equal("foto.png não é um instalador ou script (.exe, .msi, .bat ou .cmd).", PhoneProgramsText.FileProblem("foto.png", 1000, Pcs));
        Assert.Equal("setup.exe passa de 4 GB e não pode ser enviado.", PhoneProgramsText.FileProblem("setup.exe", ProgramInstall.MaxFileSize + 1, Pcs));
    }

    [Fact]
    public void A_phones_install_answer_is_shown_as_it_comes_but_an_older_ones()
    {
        Assert.Equal("instalado: Olá (versão 1.0)", PhoneProgramsText.InstallLine(true, "instalado: Olá (versão 1.0)"));
        Assert.Equal(PhoneProgramsText.OldAgent(MessageType.InstallProgram), PhoneProgramsText.InstallLine(false, "Ainda não funciona em celular."));
        Assert.Equal(PhoneProgramsText.OldAgent(MessageType.InstallProgram), PhoneProgramsText.InstallLine(false, string.Empty));
        Assert.Equal("Instalando ola.apk em 2 celular(es).", PhoneProgramsText.Installing(@"C:\apps\ola.apk", 2));
    }

    [Fact]
    public void A_phone_gets_sites_and_apps_on_its_agent_and_its_answer_shown_as_it_comes()
    {
        string? target = null;
        var result = RemoteLaunch.SendToPhone(Phone("Celular 01"), MessageType.OpenWebsite, thumbprint =>
        {
            target = thumbprint;
            return (true, "aberto no Chrome");
        });
        Assert.Equal("AB12", target);
        Assert.Equal((true, "aberto no Chrome", true), result);
    }

    [Fact]
    public void An_older_phone_a_phone_without_identity_and_one_that_is_off_are_told_apart()
    {
        Assert.Equal((false, PhoneProgramsText.OldAgent(MessageType.OpenProgram), true),
            RemoteLaunch.SendToPhone(Phone("Celular 01"), MessageType.OpenProgram, _ => (false, "Ainda não funciona em celular.")));
        Assert.Equal((false, PhoneAgentCommand.NoIdentity, false),
            RemoteLaunch.SendToPhone(Phone("Celular 01", agent: null), MessageType.OpenWebsite, _ => throw new InvalidOperationException()));
        var off = RemoteLaunch.SendToPhone(Phone("Celular 01"), MessageType.OpenWebsite,
            _ => throw new SocketException((int)SocketError.ConnectionRefused));
        Assert.False(off.Succeeded);
        Assert.False(off.Reached);
        Assert.Equal("O Midview não está rodando neste celular", off.Detail);
    }
}
