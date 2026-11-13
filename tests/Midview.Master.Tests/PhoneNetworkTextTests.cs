using Midview.Master;
using Xunit;

// How the "Liberar Wi-Fi e modo avião" window words the password and the devices' answers.
public class PhoneNetworkTextTests
{
    [Theory]
    [InlineData("abc 123!", "abc123")]
    [InlineData("Senha-da-Escola-2026", "SenhadaEscol")]
    [InlineData("çãoé12", "o12")]
    [InlineData(null, "")]
    public void The_box_keeps_letters_and_digits_up_to_twelve(string? typed, string kept) =>
        Assert.Equal(kept, PhoneNetworkText.Clean(typed));

    [Theory]
    [InlineData("", "De 6 a 12 letras ou números.")]
    [InlineData("abc", "Faltam 3 letras ou números.")]
    [InlineData("abc12", "Falta 1 letra ou número.")]
    [InlineData("abc123", "")]
    [InlineData("abcdefghijkl", "")]
    public void The_hint_says_what_is_missing(string typed, string hint) =>
        Assert.Equal(hint, PhoneNetworkText.PasswordHint(typed));

    [Theory]
    [InlineData("abc123", true)]
    [InlineData("abc12", false)]
    [InlineData("1234", false)]
    [InlineData("", false)]
    public void Only_a_whole_password_can_be_set(string typed, bool ready) =>
        Assert.Equal(ready, PhoneNetworkText.IsPasswordReady(typed));

    [Fact]
    public void One_device_or_many_in_the_heading()
    {
        Assert.Equal("Wi-Fi e modo avião do celular ou tablet", PhoneNetworkText.Heading(1));
        Assert.Equal("Wi-Fi e modo avião dos celulares e tablets", PhoneNetworkText.Heading(3));
    }

    [Theory]
    [InlineData(PhoneNetworkAction.Free, 1, "Liberando o Wi-Fi e o modo avião no aparelho...")]
    [InlineData(PhoneNetworkAction.Free, 5, "Liberando o Wi-Fi e o modo avião nos 5 aparelhos...")]
    [InlineData(PhoneNetworkAction.Lock, 5, "Travando o Wi-Fi e o modo avião nos 5 aparelhos...")]
    [InlineData(PhoneNetworkAction.SetPassword, 2, "Pondo a senha de administrador nos 2 aparelhos...")]
    [InlineData(PhoneNetworkAction.RemovePassword, 2, "Tirando a senha de administrador dos 2 aparelhos...")]
    public void Progress_says_what_is_being_done(PhoneNetworkAction action, int devices, string text) =>
        Assert.Equal(text, PhoneNetworkText.Progress(action, devices));

    [Theory]
    [InlineData(PhoneNetworkAction.Free, 10, 1, 1, "Wi-Fi e modo avião liberados por 10 minutos.")]
    [InlineData(PhoneNetworkAction.Free, 30, 4, 4, "Wi-Fi e modo avião liberados por 30 minutos nos 4 aparelhos.")]
    [InlineData(PhoneNetworkAction.Lock, 0, 3, 4, "Wi-Fi e modo avião travados em 3 dos 4 aparelhos (veja abaixo).")]
    [InlineData(PhoneNetworkAction.SetPassword, 0, 0, 4, "Não deu certo em nenhum dos 4 aparelhos (veja abaixo).")]
    [InlineData(PhoneNetworkAction.RemovePassword, 0, 4, 4, "Senha de administrador tirada dos 4 aparelhos.")]
    [InlineData(PhoneNetworkAction.SetPassword, 0, 0, 1, "Não deu certo no aparelho (veja abaixo).")]
    public void The_summary_counts_the_devices(PhoneNetworkAction action, int minutes, int done, int total, string text) =>
        Assert.Equal(text, PhoneNetworkText.Summary(action, minutes, done, total));

    [Fact]
    public void A_line_is_the_agents_answer_or_the_old_version()
    {
        Assert.Equal("Wi-Fi e modo avião travados", PhoneNetworkText.Line((true, "Wi-Fi e modo avião travados")));
        Assert.Equal(PhoneNetworkText.OldAgent, PhoneNetworkText.Line(null));
        Assert.Equal("liberando…", PhoneNetworkText.Working(PhoneNetworkAction.Free));
    }
}
