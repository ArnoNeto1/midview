using System.Globalization;
using System.Text.RegularExpressions;
using Midview.Master;
using Xunit;

// How the "Senha dos celulares e tablets" window words the PIN and the answers.
public class PhonePinTextTests
{
    [Theory]
    [InlineData("1234", "1234")]
    [InlineData("12 34", "1234")]
    [InlineData("a1b2c3d4e5", "1234")]
    [InlineData("123456", "1234")]
    [InlineData("１２３４", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void The_box_keeps_only_four_ascii_digits(string? typed, string kept) =>
        Assert.Equal(kept, PhonePinText.Digits(typed));

    [Theory]
    [InlineData("1234", true)]
    [InlineData("0000", true)]
    [InlineData("123", false)]
    [InlineData("12345", false)]
    [InlineData("12a4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_four_digits_can_be_put(string? pin, bool ready) =>
        Assert.Equal(ready, PhonePinText.IsReady(pin));

    [Theory]
    [InlineData("", "Digite 4 números.")]
    [InlineData("1", "Faltam 3 números.")]
    [InlineData("12", "Faltam 2 números.")]
    [InlineData("123", "Falta 1 número.")]
    [InlineData("1234", "")]
    public void The_hint_counts_what_is_missing(string pin, string hint) =>
        Assert.Equal(hint, PhonePinText.PinHint(pin));

    [Theory]
    [InlineData(1, "Senha do celular ou tablet")]
    [InlineData(2, "Senha dos celulares e tablets")]
    public void Heading_follows_the_count(int devices, string heading) =>
        Assert.Equal(heading, PhonePinText.Heading(devices));

    [Theory]
    [InlineData(true, 1, "Pondo o PIN no aparelho...")]
    [InlineData(true, 3, "Pondo o PIN nos 3 aparelhos...")]
    [InlineData(false, 1, "Tirando o PIN do aparelho...")]
    [InlineData(false, 3, "Tirando o PIN dos 3 aparelhos...")]
    public void Progress_follows_the_count(bool set, int devices, string text) =>
        Assert.Equal(text, PhonePinText.Progress(set, devices));

    [Theory]
    [InlineData(true, 1, 1, "PIN posto.")]
    [InlineData(true, 0, 1, "O PIN não foi posto (veja abaixo).")]
    [InlineData(true, 3, 3, "PIN posto nos 3 aparelhos.")]
    [InlineData(true, 0, 3, "O PIN não foi posto em nenhum dos 3 aparelhos (veja abaixo).")]
    [InlineData(true, 1, 3, "PIN posto em 1 dos 3 aparelhos (veja abaixo).")]
    [InlineData(true, 2, 3, "PIN posto em 2 dos 3 aparelhos (veja abaixo).")]
    [InlineData(false, 1, 1, "PIN tirado.")]
    [InlineData(false, 0, 1, "O PIN não foi tirado (veja abaixo).")]
    [InlineData(false, 3, 3, "PIN tirado dos 3 aparelhos.")]
    [InlineData(false, 0, 3, "O PIN não foi tirado de nenhum dos 3 aparelhos (veja abaixo).")]
    [InlineData(false, 1, 3, "PIN tirado de 1 dos 3 aparelhos (veja abaixo).")]
    [InlineData(false, 2, 3, "PIN tirado de 2 dos 3 aparelhos (veja abaixo).")]
    public void Summary_says_how_many_did_it(bool set, int done, int total, string text) =>
        Assert.Equal(text, PhonePinText.Summary(set, done, total));

    [Fact]
    public void Each_row_says_what_it_is_doing()
    {
        Assert.Equal("pondo o PIN…", PhonePinText.Working(true));
        Assert.Equal("tirando o PIN…", PhonePinText.Working(false));
    }

    // The same words as the agent (ScreenPassword.kt), so the pending key is recognized.
    [Fact]
    public void The_pending_key_is_the_agents_own_words() =>
        Assert.Equal("chave de redefinição ainda não ativa: desbloqueie o celular uma vez", PhonePinText.AgentTokenPending);

    [Theory]
    [InlineData(false, "Ainda não funciona em celular.", true)]
    [InlineData(false, "", true)]
    [InlineData(false, "o Android recusou o PIN", false)]
    [InlineData(true, "PIN posto no celular", false)]
    [InlineData(true, "", false)]
    public void An_older_agent_is_one_that_fails_a_command_it_does_not_know(bool succeeded, string detail, bool old) =>
        Assert.Equal(old, PhonePinText.IsOldAgent(succeeded, detail));

    // The window explains the rules and never opens taller than a small screen's work area.
    [Fact]
    public void The_window_explains_the_rules_and_fits_a_small_screen()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Midview.Master", "PhonePinWindow.xaml"));

        Assert.Contains("MaxHeight=\"{Binding Source={x:Static SystemParameters.WorkArea}, Path=Height}\"", xaml);
        var minHeight = double.Parse(Regex.Match(xaml, "MinHeight=\"(\\d+)\"").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(minHeight <= 672, $"MinHeight {minHeight} would not fit a small screen");
        Assert.Contains("O aluno não consegue trocar nem tirar o PIN", xaml);
        Assert.Contains("use esta janela, não o celular", xaml);
        Assert.Contains("precisa ser desbloqueado uma vez antes", xaml);
        Assert.Contains("antes da versão 0.0.57 precisam ser reiniciados uma vez", xaml);
        Assert.Contains("MaxLength=\"4\"", xaml);
        Assert.Contains("Content=\"Pôr o PIN\"", xaml);
        Assert.Contains("Content=\"Tirar o PIN\"", xaml);
        Assert.Contains("Content=\"Mostrar\"", xaml);
    }

    // Nothing in the window or the client writes the PIN anywhere: the teacher types it each time.
    [Fact]
    public void The_pin_is_never_saved_on_the_pc()
    {
        foreach (var file in new[] { "PhonePinWindow.xaml.cs", "PhonePinClient.cs", "PhonePinText.cs" })
        {
            var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Midview.Master", file));
            Assert.DoesNotContain("File.", code);
            Assert.DoesNotContain("MasterData", code);
            Assert.DoesNotContain("Registry", code);
        }
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
