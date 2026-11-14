using Midview.Master;
using Xunit;

// How the "Contas do aparelho" and "Sair das contas do Chrome" windows word the devices' answers.
public class PhoneAccountsTextTests
{
    [Fact]
    public void One_device_or_many_in_the_heading()
    {
        Assert.Equal("Contas do celular ou tablet", PhoneAccountsText.Heading(PhoneAccountsAction.Free, 1));
        Assert.Equal("Contas dos celulares e tablets", PhoneAccountsText.Heading(PhoneAccountsAction.Lock, 3));
        Assert.Equal("Sair das contas do Chrome dos celulares e tablets", PhoneAccountsText.Heading(PhoneAccountsAction.ClearChrome, 3));
    }

    [Theory]
    [InlineData(PhoneAccountsAction.Free, 1, "Liberando as contas no aparelho...")]
    [InlineData(PhoneAccountsAction.Lock, 5, "Travando as contas nos 5 aparelhos...")]
    [InlineData(PhoneAccountsAction.ClearChrome, 2, "Saindo das contas do Chrome nos 2 aparelhos...")]
    public void Progress_says_what_is_being_done(PhoneAccountsAction action, int devices, string text) =>
        Assert.Equal(text, PhoneAccountsText.Progress(action, devices));

    [Theory]
    [InlineData(PhoneAccountsAction.Free, 10, 1, 1, "Contas liberadas por 10 minutos.")]
    [InlineData(PhoneAccountsAction.Free, 30, 4, 4, "Contas liberadas por 30 minutos nos 4 aparelhos.")]
    [InlineData(PhoneAccountsAction.Lock, 0, 3, 4, "Contas travadas em 3 dos 4 aparelhos (veja abaixo).")]
    [InlineData(PhoneAccountsAction.ClearChrome, 0, 0, 4, "Não deu certo em nenhum dos 4 aparelhos (veja abaixo).")]
    [InlineData(PhoneAccountsAction.ClearChrome, 0, 4, 4, "Chrome limpo nos 4 aparelhos.")]
    [InlineData(PhoneAccountsAction.ClearChrome, 0, 0, 1, "Não deu certo no aparelho (veja abaixo).")]
    public void The_summary_counts_the_devices(PhoneAccountsAction action, int minutes, int done, int total, string text) =>
        Assert.Equal(text, PhoneAccountsText.Summary(action, minutes, done, total));

    // It cannot be undone, and the next student meets Chrome's first screen.
    [Fact]
    public void Clearing_chrome_warns_first()
    {
        Assert.Contains("não dá para desfazer", PhoneAccountsText.ClearChromeConfirm(3));
        Assert.StartsWith("Sair das contas do Chrome nos 3 aparelhos?", PhoneAccountsText.ClearChromeConfirm(3));
        Assert.StartsWith("Sair das contas do Chrome no aparelho?", PhoneAccountsText.ClearChromeConfirm(1));
        Assert.Contains("Não dá para desfazer", PhoneAccountsText.ClearChromeExplanation);
        Assert.Contains("\"Continuar sem fazer login\"", PhoneAccountsText.ClearChromeExplanation);
    }

    [Fact]
    public void A_line_is_the_agents_answer_or_the_old_version()
    {
        Assert.Equal("contas do aparelho travadas", PhoneAccountsText.Line((true, "contas do aparelho travadas")));
        Assert.Equal(PhoneAccountsText.OldAgent, PhoneAccountsText.Line(null));
    }
}
