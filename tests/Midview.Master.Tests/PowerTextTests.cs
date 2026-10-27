using Midview.Master;
using Xunit;

public class PowerTextTests
{
    [Theory]
    [InlineData(false, false, "Desligar")]
    [InlineData(true, false, "Reiniciar")]
    [InlineData(false, true, "Desligar instalando as atualizações")]
    [InlineData(true, true, "Reiniciar instalando as atualizações")]
    public void TheRunButton_SaysWhatItDoes(bool restart, bool installUpdates, string text) =>
        Assert.Equal(text, PowerText.RunButton(restart, installUpdates));

    [Fact]
    public void TheUpdatesHint_NamesWindowsOwnOption()
    {
        Assert.Equal(
            "Como \"Atualizar e reiniciar\" no menu Iniciar do Windows: instala só o que o Windows já baixou. " +
            "Pode levar alguns minutos a mais em cada máquina. Máquinas sem atualização esperando só reiniciam.",
            PowerText.UpdatesHint(restart: true));
        Assert.Contains("\"Atualizar e desligar\"", PowerText.UpdatesHint(restart: false));
        Assert.EndsWith("só desligam.", PowerText.UpdatesHint(restart: false));
    }

    // An Agent from before refuses the longer request as invalid: say why.
    [Fact]
    public void AnOldAgent_RefusingTheUpdates_IsToldApart() =>
        Assert.Equal(
            "Midview antigo nesta máquina: atualize-a pela rede (Máquinas da sala) para instalar as atualizações ao desligar",
            PowerText.Detail(succeeded: false, "pedido inválido", installUpdates: true));

    [Theory]
    [InlineData(false, "pedido inválido", false, "pedido inválido")]
    [InlineData(true, "reinicia em 1 minuto, instalando as atualizações", true, "reinicia em 1 minuto, instalando as atualizações")]
    [InlineData(false, "Offline", true, "Offline")]
    public void AnyOtherAnswer_IsShownAsItIs(bool succeeded, string detail, bool installUpdates, string shown) =>
        Assert.Equal(shown, PowerText.Detail(succeeded, detail, installUpdates));
}
