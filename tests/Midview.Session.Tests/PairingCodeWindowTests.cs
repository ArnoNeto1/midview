using Midview.Session;
using Xunit;

public class PairingCodeWindowTests
{
    [Fact]
    public void Texts_AreTheSpecs()
    {
        var (title, detail, code, footer) = PairingCodeWindow.Texts(482913, "PROF-01");

        Assert.Equal("Pareamento com o professor", title);
        Assert.Equal("O computador PROF-01 está conectando esta máquina ao Midview.", detail);
        Assert.Equal("482 913", code);
        Assert.Equal("O professor confere se o mesmo código aparece na tela dele.", footer);
    }
}
