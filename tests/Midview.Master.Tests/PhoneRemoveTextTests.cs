using System.Globalization;
using System.Text.RegularExpressions;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// How the "Tirar o Midview dos celulares e tablets" window words the two
// choices, the word that confirms each one, and the devices' answers.
public class PhoneRemoveTextTests
{
    [Fact]
    public void One_device_or_many_in_the_heading()
    {
        Assert.Equal("Tirar o Midview do celular ou tablet", PhoneRemoveText.Heading(1));
        Assert.Equal("Tirar o Midview dos celulares e tablets", PhoneRemoveText.Heading(3));
    }

    [Fact]
    public void Each_choice_says_what_happens_to_the_students_files()
    {
        Assert.Equal("Desfazer e tirar o Midview", PhoneRemoveText.Title(PhoneRemoveChoice.Undo));
        Assert.Equal("Restaurar de fábrica", PhoneRemoveText.Title(PhoneRemoveChoice.FactoryReset));
        Assert.StartsWith("Mantém as fotos e os arquivos dos alunos.", PhoneRemoveText.Explanation(PhoneRemoveChoice.Undo));
        Assert.Contains("o Midview se desinstala", PhoneRemoveText.Explanation(PhoneRemoveChoice.Undo));
        // What the undo cannot bring back is said up front.
        Assert.Contains("Não voltam a conta Mi", PhoneRemoveText.Explanation(PhoneRemoveChoice.Undo));
        Assert.StartsWith("Apaga tudo", PhoneRemoveText.Explanation(PhoneRemoveChoice.FactoryReset));
        Assert.Contains("Não dá para desfazer.", PhoneRemoveText.Explanation(PhoneRemoveChoice.FactoryReset));
    }

    [Fact]
    public void Each_choice_has_its_own_word()
    {
        Assert.Equal("REMOVER", PhoneRemoveText.Word(PhoneRemoveChoice.Undo));
        Assert.Equal("APAGAR", PhoneRemoveText.Word(PhoneRemoveChoice.FactoryReset));
        Assert.Equal("Para confirmar, digite REMOVER:", PhoneRemoveText.Prompt(PhoneRemoveChoice.Undo));
        Assert.Equal("Para confirmar, digite APAGAR:", PhoneRemoveText.Prompt(PhoneRemoveChoice.FactoryReset));
        Assert.Equal("Tirar o Midview", PhoneRemoveText.Button(PhoneRemoveChoice.Undo));
        Assert.Equal("Apagar os aparelhos", PhoneRemoveText.Button(PhoneRemoveChoice.FactoryReset));
    }

    [Theory]
    [InlineData(PhoneRemoveChoice.Undo, "REMOVER", true)]
    [InlineData(PhoneRemoveChoice.Undo, "remover", true)]
    [InlineData(PhoneRemoveChoice.Undo, "  Remover ", true)]
    [InlineData(PhoneRemoveChoice.Undo, "REMOVE", false)]
    [InlineData(PhoneRemoveChoice.Undo, "APAGAR", false)]
    [InlineData(PhoneRemoveChoice.Undo, "", false)]
    [InlineData(PhoneRemoveChoice.Undo, null, false)]
    [InlineData(PhoneRemoveChoice.FactoryReset, "APAGAR", true)]
    [InlineData(PhoneRemoveChoice.FactoryReset, "apagar", true)]
    [InlineData(PhoneRemoveChoice.FactoryReset, "REMOVER", false)]
    [InlineData(PhoneRemoveChoice.FactoryReset, "APAGA", false)]
    public void Only_the_choices_own_word_confirms_it(PhoneRemoveChoice choice, string? typed, bool confirmed) =>
        Assert.Equal(confirmed, PhoneRemoveText.IsConfirmed(choice, typed));

    [Fact]
    public void An_older_agent_is_told_apart_for_each_choice()
    {
        Assert.True(PhoneRemoveText.IsOldAgent(false, "Ainda não funciona em celular."));
        Assert.True(PhoneRemoveText.IsOldAgent(false, ""));
        Assert.False(PhoneRemoveText.IsOldAgent(false, "Este celular não foi preparado pelo Midview."));
        Assert.Equal(PhoneRemoveText.OldAgent, PhoneRemoveText.Line(PhoneRemoveChoice.Undo, null));
        Assert.Equal(PhoneRemoveText.OldAgentWipe, PhoneRemoveText.Line(PhoneRemoveChoice.FactoryReset, null));
    }

    [Fact]
    public void Each_line_says_how_the_device_answered()
    {
        Assert.Equal("Midview saindo: travas e ajustes da escola desfeitos.",
            PhoneRemoveText.Line(PhoneRemoveChoice.Undo, (true, "Midview saindo: travas e ajustes da escola desfeitos.")));
        Assert.Equal(PhoneRemoveText.WipeStarted, PhoneRemoveText.Line(PhoneRemoveChoice.FactoryReset, (true, "apagando")));
        Assert.Equal("Este celular não foi preparado pelo Midview.",
            PhoneRemoveText.Line(PhoneRemoveChoice.FactoryReset, (false, "Este celular não foi preparado pelo Midview.")));
    }

    [Theory]
    [InlineData(PhoneRemoveChoice.Undo, 1, "Tirando o Midview do aparelho...")]
    [InlineData(PhoneRemoveChoice.Undo, 4, "Tirando o Midview dos 4 aparelhos...")]
    [InlineData(PhoneRemoveChoice.FactoryReset, 1, "Pedindo ao aparelho para voltar de fábrica...")]
    [InlineData(PhoneRemoveChoice.FactoryReset, 4, "Pedindo aos 4 aparelhos para voltar de fábrica...")]
    public void The_progress_says_what_is_being_done(PhoneRemoveChoice choice, int devices, string said) =>
        Assert.Equal(said, PhoneRemoveText.Progress(choice, devices));

    [Theory]
    [InlineData(PhoneRemoveChoice.Undo, 1, 1, "Midview tirado. O aparelho saiu da lista da sala.")]
    [InlineData(PhoneRemoveChoice.Undo, 0, 1, "Não deu certo no aparelho (veja abaixo).")]
    [InlineData(PhoneRemoveChoice.Undo, 3, 3, "Midview tirado nos 3 aparelhos. Eles saíram da lista da sala.")]
    [InlineData(PhoneRemoveChoice.Undo, 0, 3, "Não deu certo em nenhum dos 3 aparelhos (veja abaixo).")]
    [InlineData(PhoneRemoveChoice.FactoryReset, 2, 3, "Pedido para voltar de fábrica em 2 dos 3 aparelhos; esses saíram da lista da sala (veja abaixo).")]
    public void The_summary_says_how_many_did_it_and_left_the_room(PhoneRemoveChoice choice, int done, int total, string said) =>
        Assert.Equal(said, PhoneRemoveText.Summary(choice, done, total));

    // A small screen's work area: the window is held to it, its list scrolls.
    [Fact]
    public void The_window_never_opens_taller_than_the_work_area()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Midview.Master", "PhoneRemoveWindow.xaml"));

        Assert.Contains("MaxHeight=\"{Binding Source={x:Static SystemParameters.WorkArea}, Path=Height}\"", xaml);
        var minHeight = double.Parse(Regex.Match(xaml, "MinHeight=\"(\\d+)\"").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(minHeight <= 672, $"MinHeight {minHeight} would not fit a small screen");
        // The button starts greyed: nothing goes before the word is typed.
        Assert.Matches("x:Name=\"GoButton\"[^>]*IsEnabled=\"False\"", xaml);
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
