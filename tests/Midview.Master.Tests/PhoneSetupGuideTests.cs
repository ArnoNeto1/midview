using System.Reflection;
using System.Resources;
using Midview.Master;
using Xunit;

public class PhoneSetupGuideTests
{
    private static readonly string[] Banned = { "Device Owner", "APK", "provision", "adb", "DPC" };

    private static IEnumerable<string> AllTexts()
    {
        foreach (var s in PhoneSetupGuide.Steps.Append(PhoneSetupGuide.ChipOnly))
        {
            yield return s.Title;
            yield return s.NextText;
            if (s.Timing != null) yield return s.Timing;
            foreach (var t in s.Taps) yield return t;
            foreach (var h in s.Help) { yield return h.Problem; yield return h.Answer; }
        }
        yield return PhoneSetupGuide.MiAccountIntro;
        yield return PhoneSetupGuide.MiAccountCreate;
        foreach (var h in PhoneSetupGuide.MiAccountHelp) { yield return h.Problem; yield return h.Answer; }
        yield return PhoneSetupGuide.Gather.Title;
        foreach (var item in PhoneSetupGuide.Gather.Items) yield return item;
        foreach (var h in new[] { PhoneSetupGuide.UnknownAccount, PhoneSetupGuide.LockedWithPin })
        {
            yield return h.Problem; yield return h.Answer;
        }
        foreach (var h in PhoneSetupGuide.NotAppeared.Concat(PhoneSetupGuide.WifiHelp)
                     .Concat(PhoneSetupGuide.NotAppearedFor("Celular 07", ownerUnconfirmed: true)))
        {
            yield return h.Problem; yield return h.Answer;
        }
        yield return PhoneSetupGuide.NotAppearedHint(false);
        yield return PhoneSetupGuide.NotAppearedHint(true);
    }

    private static HashSet<string> ResourceNames()
    {
        var asm = typeof(PhoneSetupGuide).Assembly;
        using var stream = asm.GetManifestResourceStream(asm.GetName().Name + ".g.resources")!;
        using var reader = new ResourceReader(stream);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in reader) names.Add((string)e.Key);
        return names;
    }

    [Fact]
    public void Steps_come_in_the_expected_order()
    {
        Assert.Equal(
            new[] { "numero", "ligar", "desenvolvedor", "chip-conta-mi", "chaves", "chrome", "conta-google", "sair-contas", "cabo" },
            PhoneSetupGuide.Steps.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Chrome_is_updated_with_the_cable_allowed_and_before_the_google_account_leaves()
    {
        var ids = PhoneSetupGuide.Steps.Select(s => s.Id).ToList();
        // "Depuração USB" (chaves) lets the Midview read it; the Google account lets the Play Store update it.
        Assert.True(ids.IndexOf("chaves") < ids.IndexOf("chrome"));
        Assert.True(ids.IndexOf("chrome") < ids.IndexOf("conta-google"));
        Assert.True(ids.IndexOf("conta-google") < ids.IndexOf("sair-contas"));
        Assert.True(ids.IndexOf("sair-contas") < ids.IndexOf("cabo"));
    }

    private static int Number(string id) => PhoneSetupGuide.Steps.Select(s => s.Id).ToList().IndexOf(id) + 1;

    private static string Texts(GuideStep step) =>
        string.Join(" ", step.Taps.Concat(step.Help.Select(h => h.Answer)));

    [Fact]
    public void Step_numbers_in_the_texts_point_to_the_right_steps()
    {
        var chaves = Texts(PhoneSetupGuide.Steps.Single(s => s.Id == "chaves"));
        Assert.Contains($"Volte ao passo {Number("chip-conta-mi")}, confira os dois", chaves);
        Assert.Contains($"Falta o chip. Volte ao passo {Number("chip-conta-mi")}", chaves);
        Assert.Contains($"Falta ligar o modo de desenvolvedor. Volte ao passo {Number("desenvolvedor")}.", chaves);
        var google = $"como no passo {Number("conta-google")}:";
        Assert.Contains(google, Texts(PhoneSetupGuide.Steps.Single(s => s.Id == "sair-contas")));
        Assert.Contains($"(passo {Number("conta-google")})", Texts(PhoneSetupGuide.Steps.Single(s => s.Id == "sair-contas")));
        Assert.Contains(google, Texts(PhoneSetupGuide.ChipOnly));
        Assert.Contains($"como no passo {Number("chrome")}", Texts(PhoneSetupGuide.Steps.Single(s => s.Id == "cabo")));
        var skip = PhoneSetupGuide.MiAccountHelp.Single(h => h.Problem == "Posso pular?").Answer;
        Assert.Contains($"no passo {Number("chip-conta-mi")}, você entra", skip);
        Assert.Contains($"no passo {Number("sair-contas")} sai dela", skip);
        Assert.Contains($"volte ao passo {Number("ligar")}", PhoneSetupGuide.LockedWithPin.Answer);
    }

    [Fact]
    public void The_chrome_step_says_what_to_tap_and_that_the_google_account_leaves_next()
    {
        var step = PhoneSetupGuide.Steps.Single(s => s.Id == "chrome");
        var text = string.Join(" ", step.Taps);
        Assert.Equal("Atualize o Chrome", step.Title);
        Assert.Contains("Permitir depuração USB?", text);
        Assert.Contains("toque em Atualizar", text);
        Assert.Contains("Android System WebView", text);
        Assert.Contains("Ela sai no próximo passo", text);
        Assert.Contains(step.Help, h => h.Problem == "Não tenho conta Google" && h.Answer.Contains("Pular"));
        Assert.Contains("conta Google", string.Join(" ", PhoneSetupGuide.Gather.Items));
    }

    [Fact]
    public void A_skipped_chrome_warns_on_the_ready_page_and_an_updated_one_does_not()
    {
        var old = new ChromeVersions(126, 126);
        Assert.Null(PhoneSetupGuide.ChromeWarning(false, false, new ChromeVersions(131, 131)));
        Assert.Equal(
            "Atenção: o Chrome deste celular não foi atualizado (está na versão 126; precisa da 131 ou mais nova), " +
            "e o bloqueio de downloads não vai funcionar nele. Para atualizar depois, é preciso apagar o celular e prepará-lo de novo.",
            PhoneSetupGuide.ChromeWarning(true, true, old));
        Assert.Contains("não foi atualizado, e o bloqueio", PhoneSetupGuide.ChromeWarning(true, true, null));
        Assert.Equal(
            "Atenção: o Android System WebView deste celular não foi atualizado (está na versão 126). " +
            "Para atualizar depois, é preciso apagar o celular e prepará-lo de novo.",
            PhoneSetupGuide.ChromeWarning(false, true, new ChromeVersions(131, 126)));
    }

    [Fact]
    public void Every_step_has_help_and_taps_except_the_number()
    {
        foreach (var s in PhoneSetupGuide.Steps)
        {
            Assert.NotEmpty(s.Help);
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.NextText));
            if (s.Id != "numero") Assert.NotEmpty(s.Taps);
        }
    }

    [Fact]
    public void Every_image_exists_as_a_resource()
    {
        var names = ResourceNames();
        foreach (var s in PhoneSetupGuide.Steps.Where(s => s.Image != null))
        {
            Assert.Contains(s.Image!.ToLowerInvariant(), names);
        }
        foreach (var f in new[] { "configuracoes", "contas", "sobre", "desenvolvedor", "conta-mi" })
        {
            Assert.Contains("assets/prepararcelular/" + f + ".png", names);
        }
    }

    [Fact]
    public void Every_target_is_inside_the_image()
    {
        foreach (var s in PhoneSetupGuide.Steps.Where(s => s.Target != null))
        {
            var r = s.Target!.Value;
            Assert.NotNull(s.Image);
            Assert.InRange(r.Left, 0, 1);
            Assert.InRange(r.Top, 0, 1);
            Assert.InRange(r.Right, 0, 1);
            Assert.InRange(r.Bottom, 0, 1);
            Assert.True(r.Width > 0 && r.Height > 0);
        }
    }

    [Fact]
    public void Texts_have_no_jargon()
    {
        foreach (var text in AllTexts())
        {
            foreach (var word in Banned)
            {
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void The_number_step_asks_with_the_spec_words() =>
        Assert.Equal("Qual número está escrito atrás deste celular?", PhoneSetupGuide.Steps.Single(s => s.Id == "numero").Title);

    [Fact]
    public void Fixed_helps_are_filled()
    {
        Assert.False(string.IsNullOrWhiteSpace(PhoneSetupGuide.UnknownAccount.Answer));
        Assert.False(string.IsNullOrWhiteSpace(PhoneSetupGuide.LockedWithPin.Answer));
        Assert.NotEmpty(PhoneSetupGuide.NotAppeared);
        Assert.NotEmpty(PhoneSetupGuide.WifiHelp);
    }

    [Fact]
    public void An_answer_that_quotes_a_fixed_help_links_to_it()
    {
        foreach (var fixedHelp in new[] { PhoneSetupGuide.UnknownAccount, PhoneSetupGuide.LockedWithPin })
        {
            foreach (var help in PhoneSetupGuide.Steps.SelectMany(s => s.Help))
            {
                if (help.Answer.Contains('"' + fixedHelp.Problem + '"'))
                {
                    Assert.Contains(fixedHelp, help.Links ?? Array.Empty<GuideHelp>());
                }
            }
        }
    }

    [Fact]
    public void Gather_names_what_to_bring()
    {
        var all = PhoneSetupGuide.Gather.Title + " " + string.Join(" ", PhoneSetupGuide.Gather.Items);
        Assert.StartsWith("Antes de começar, separe", PhoneSetupGuide.Gather.Title);
        foreach (var word in new[] { "cabo", "chip", "agulha", "conta Mi da escola" })
        {
            Assert.Contains(word, all, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Menu_steps_point_to_the_settings_search_in_their_help()
    {
        foreach (var id in new[] { "conta-google", "desenvolvedor", "chip-conta-mi", "chaves", "sair-contas" })
        {
            var step = PhoneSetupGuide.Steps.Single(s => s.Id == id);
            Assert.Contains(step.Help, h => h.Answer.Contains("Pesquisar nas Configurações"));
        }
    }

    [Fact]
    public void Not_appeared_after_an_unconfirmed_owner_says_to_prepare_again_if_the_phone_did_not_say_ready()
    {
        var helps = PhoneSetupGuide.NotAppearedFor("Celular 07", ownerUnconfirmed: true);
        var text = string.Join(" ", helps.Select(h => h.Problem + " " + h.Answer));
        Assert.Contains("Se o celular não mostrou \"Pronto. Este é o Celular 07 da escola.\", ligue o cabo de novo e prepare outra vez.", text);
        Assert.DoesNotContain("não adianta", text);
        Assert.DoesNotContain("já confia", PhoneSetupGuide.NotAppearedHint(true));
        // The network helps stay.
        Assert.Contains("mesmo Wi-Fi", text);
        Assert.Contains("roteador", text);
    }

    [Fact]
    public void Not_appeared_after_a_confirmed_owner_is_unchanged()
    {
        Assert.Same(PhoneSetupGuide.NotAppeared, PhoneSetupGuide.NotAppearedFor("Celular 07", ownerUnconfirmed: false));
        Assert.Contains("já confia", PhoneSetupGuide.NotAppearedHint(false));
    }

    [Fact]
    public void The_cable_step_helps_when_another_phone_program_is_open()
    {
        var step = PhoneSetupGuide.Steps.Single(s => s.Id == "cabo");
        Assert.Contains(step.Help, h =>
            h.Answer == "Feche outros programas de celular abertos neste computador e ligue o cabo de novo.");
    }

    [Fact]
    public void The_cable_step_warns_about_both_questions()
    {
        var step = PhoneSetupGuide.Steps.Single(s => s.Id == "cabo");
        var text = string.Join(" ", step.Taps);
        Assert.Contains("Permitir depuração USB?", text);
        Assert.Contains("Sempre permitir deste computador", text);
        Assert.Contains("Permitir", text);
        Assert.Contains("Instalar", text);
    }

    [Fact]
    public void The_pin_help_is_the_recovery_menu()
    {
        Assert.Equal("Celular travado com PIN", PhoneSetupGuide.LockedWithPin.Problem);
        Assert.Contains("Wipe All Data", PhoneSetupGuide.LockedWithPin.Answer);
    }

    [Fact]
    public void Not_appeared_is_about_the_network_and_says_the_phone_already_trusts_this_pc()
    {
        var text = string.Join(" ", PhoneSetupGuide.NotAppeared.Select(h => h.Problem + " " + h.Answer));
        Assert.Contains("mesmo Wi-Fi", text);
        Assert.Contains("roteador", text);
        Assert.Contains("já confia", text);
    }

    [Fact]
    public void No_text_mentions_the_old_qr_or_six_taps()
    {
        foreach (var text in AllTexts())
        {
            Assert.DoesNotContain("QR", text);
            Assert.DoesNotContain("6 vezes", text);
            Assert.DoesNotContain("Recomeçar este celular", text);
        }
    }

    [Fact]
    public void No_text_names_old_miui_versions()
    {
        foreach (var text in AllTexts())
        {
            Assert.DoesNotContain("MIUI 12", text);
        }
    }
}
