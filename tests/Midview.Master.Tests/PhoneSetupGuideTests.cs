using System.Reflection;
using System.Resources;
using Midview.Master;
using Xunit;

public class PhoneSetupGuideTests
{
    private static readonly string[] Banned = { "Device Owner", "APK", "provision", "adb", "DPC" };

    private static IEnumerable<string> AllTexts()
    {
        foreach (var s in PhoneSetupGuide.Steps)
        {
            yield return s.Title;
            yield return s.NextText;
            if (s.Timing != null) yield return s.Timing;
            foreach (var t in s.Taps) yield return t;
            foreach (var h in s.Help) { yield return h.Problem; yield return h.Answer; }
        }
        foreach (var h in new[] { PhoneSetupGuide.UnknownAccount, PhoneSetupGuide.ResetWithoutPin })
        {
            yield return h.Problem; yield return h.Answer;
        }
        foreach (var h in PhoneSetupGuide.NotAppeared.Concat(PhoneSetupGuide.WifiHelp))
        {
            yield return h.Problem; yield return h.Answer;
        }
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
            new[] { "numero", "bateria", "conta-mi", "conta-google", "restaurar", "seis-toques" },
            PhoneSetupGuide.Steps.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Both_accounts_come_before_the_reset()
    {
        var ids = PhoneSetupGuide.Steps.Select(s => s.Id).ToList();
        Assert.True(ids.IndexOf("conta-mi") < ids.IndexOf("restaurar"));
        Assert.True(ids.IndexOf("conta-google") < ids.IndexOf("restaurar"));
    }

    [Fact]
    public void Account_steps_say_why_they_come_before_the_reset()
    {
        foreach (var id in new[] { "conta-mi", "conta-google" })
        {
            var step = PhoneSetupGuide.Steps.Single(s => s.Id == id);
            Assert.Contains(step.Taps, t => t.Contains("antes de restaurar", StringComparison.OrdinalIgnoreCase)
                && t.Contains("conta", StringComparison.OrdinalIgnoreCase));
        }
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
        foreach (var f in new[] { "configuracoes", "contas", "sobre", "redefinicao", "conta-mi", "bem-vindo" })
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

    // No timing that nobody measured.
    [Fact]
    public void The_six_taps_help_has_no_made_up_time_limit() =>
        Assert.DoesNotContain(PhoneSetupGuide.Steps.Single(s => s.Id == "seis-toques").Help, h => h.Answer.Contains("menos de 3 segundos"));

    [Fact]
    public void Fixed_helps_are_filled()
    {
        Assert.False(string.IsNullOrWhiteSpace(PhoneSetupGuide.UnknownAccount.Answer));
        Assert.False(string.IsNullOrWhiteSpace(PhoneSetupGuide.ResetWithoutPin.Answer));
        Assert.NotEmpty(PhoneSetupGuide.NotAppeared);
        Assert.NotEmpty(PhoneSetupGuide.WifiHelp);
    }

    [Fact]
    public void An_answer_that_quotes_a_fixed_help_links_to_it()
    {
        foreach (var fixedHelp in new[] { PhoneSetupGuide.UnknownAccount, PhoneSetupGuide.ResetWithoutPin })
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
    public void The_restore_step_says_a_phone_that_was_already_a_school_phone_needs_recovery_mode()
    {
        var help = PhoneSetupGuide.Steps.Single(s => s.Id == "restaurar").Help
            .Single(h => h.Problem.Contains("já era um celular da escola"));

        Assert.Contains("Restaurar sem o PIN", help.Answer);
        Assert.Contains(PhoneSetupGuide.ResetWithoutPin, help.Links!);
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
