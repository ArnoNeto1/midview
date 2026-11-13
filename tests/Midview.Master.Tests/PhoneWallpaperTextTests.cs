using Midview.Master;
using Midview.Protocol;
using Xunit;

// How the "Papel de parede dos celulares e tablets" window words the picture,
// the screens and the devices' answers.
public class PhoneWallpaperTextTests
{
    [Fact]
    public void One_device_or_many_in_the_heading()
    {
        Assert.Equal("Papel de parede do celular ou tablet", PhoneWallpaperText.Heading(1));
        Assert.Equal("Papel de parede dos celulares e tablets", PhoneWallpaperText.Heading(4));
    }

    [Fact]
    public void Both_screens_come_first_then_each_alone()
    {
        Assert.Equal(
            new[] { PhoneWallpaperScreens.Both, PhoneWallpaperScreens.Home, PhoneWallpaperScreens.Lock },
            PhoneWallpaperText.Choices);
        Assert.Equal(
            new[] { "Tela inicial e de bloqueio", "Só tela inicial", "Só tela de bloqueio" },
            PhoneWallpaperText.Choices.Select(PhoneWallpaperText.Choice));
    }

    [Theory]
    [InlineData(300, "1 KB")]
    [InlineData(460_000, "449 KB")]
    [InlineData(1_572_864, "1,5 MB")]
    public void The_size_is_in_kb_or_mb(int bytes, string said) => Assert.Equal(said, PhoneWallpaperText.Size(bytes));

    [Fact]
    public void The_picture_line_says_its_name_and_the_size_it_goes_at() =>
        Assert.Equal(
            "regras.jpg: vai com 1080 × 2400 pixels (449 KB)",
            PhoneWallpaperText.Picture("regras.jpg", 1080, 2400, 460_000));

    [Fact]
    public void An_older_agent_is_told_apart()
    {
        Assert.True(PhoneWallpaperText.IsOldAgent(false, "Ainda não funciona em celular."));
        Assert.True(PhoneWallpaperText.IsOldAgent(false, ""));
        Assert.False(PhoneWallpaperText.IsOldAgent(false, "o Android não trocou o papel de parede"));
        Assert.Equal(PhoneWallpaperText.OldAgent, PhoneWallpaperText.Line(null));
        Assert.Equal("papel de parede posto na tela inicial", PhoneWallpaperText.Line((true, "papel de parede posto na tela inicial")));
    }

    [Theory]
    [InlineData(false, 1, "Pondo o papel de parede no aparelho...")]
    [InlineData(false, 5, "Pondo o papel de parede nos 5 aparelhos...")]
    [InlineData(true, 1, "Voltando o papel de parede do aparelho ao padrão...")]
    [InlineData(true, 5, "Voltando o papel de parede dos 5 aparelhos ao padrão...")]
    public void The_progress_says_what_is_being_done(bool restore, int devices, string said) =>
        Assert.Equal(said, PhoneWallpaperText.Progress(restore, devices));

    [Theory]
    [InlineData(false, 1, 1, "Papel de parede posto.")]
    [InlineData(false, 0, 1, "Não deu certo no aparelho (veja abaixo).")]
    [InlineData(false, 3, 3, "Papel de parede posto nos 3 aparelhos.")]
    [InlineData(false, 0, 3, "Não deu certo em nenhum dos 3 aparelhos (veja abaixo).")]
    [InlineData(false, 2, 3, "Papel de parede posto em 2 dos 3 aparelhos (veja abaixo).")]
    [InlineData(true, 1, 1, "Papel de parede de volta ao padrão.")]
    [InlineData(true, 4, 4, "Papel de parede de volta ao padrão nos 4 aparelhos.")]
    public void The_summary_counts_the_devices(bool restore, int done, int total, string said) =>
        Assert.Equal(said, PhoneWallpaperText.Summary(restore, done, total));
}
