using Midview.Master;
using Xunit;

// The grid's multi-selection rules, tested on plain strings standing in for the
// tiles, in display order.
public sealed class GridSelectionTests
{
    private static GridSelection<string> Grid(params string[] items) => new(items);

    [Fact]
    public void PlainClick_SelecionaSoAquele_EViraAAncora()
    {
        var grid = Grid("A", "B", "C", "D");

        grid.Click("B");

        Assert.Equal(new[] { "B" }, grid.Selected);
        Assert.Equal("B", grid.Anchor);
    }

    [Fact]
    public void CtrlClick_MarcaEDesmarcaSemLimparOResto()
    {
        var grid = Grid("A", "B", "C", "D");

        grid.Click("A");      // {A}
        grid.CtrlClick("C");  // {A, C}
        grid.CtrlClick("A");  // {C}  -- A desmarcado, não era a âncora

        Assert.Equal(new[] { "C" }, grid.Selected);
        Assert.Equal("C", grid.Anchor);
    }

    [Fact]
    public void ShiftClick_PegaAFaixaDaAncoraAteOAlvo_NaOrdemDaGrade()
    {
        var grid = Grid("A", "B", "C", "D");

        grid.Click("B");       // âncora = B
        grid.ShiftClick("D");  // B..D

        Assert.Equal(new[] { "B", "C", "D" }, grid.Selected);
    }

    [Fact]
    public void ShiftClick_FaixaFuncionaDeTrasParaFrente()
    {
        var grid = Grid("A", "B", "C", "D");

        grid.Click("D");       // âncora = D
        grid.ShiftClick("B");  // B..D, mesma faixa

        Assert.Equal(new[] { "B", "C", "D" }, grid.Selected);
    }

    [Fact]
    public void CtrlClick_QuandoTiraAPropriaAncora_Reaponta()
    {
        var grid = Grid("A", "B", "C", "D");

        grid.Click("B");      // âncora = B, {B}
        grid.CtrlClick("D");  // {B, D}, âncora D
        grid.CtrlClick("D");  // tira D (a âncora); sobra {B}

        Assert.Equal(new[] { "B" }, grid.Selected);
        Assert.Equal("B", grid.Anchor);
    }

    [Fact]
    public void ShiftClick_SemAncora_ValeComoCliqueSimples()
    {
        var grid = Grid("A", "B", "C", "D");

        grid.ShiftClick("C");

        Assert.Equal(new[] { "C" }, grid.Selected);
        Assert.Equal("C", grid.Anchor);
    }

    [Fact]
    public void Prune_TiraOQueSaiuDaGrade_EReapontaAAncora()
    {
        var items = new List<string> { "A", "B", "C", "D" };
        var grid = new GridSelection<string>(items);
        grid.Click("B");      // {B}, âncora B
        grid.CtrlClick("D");  // {B, D}, âncora D

        items.Remove("D");    // D saiu da sala
        grid.Prune();

        Assert.Equal(new[] { "B" }, grid.Selected);
        Assert.Equal("B", grid.Anchor);
    }
}
