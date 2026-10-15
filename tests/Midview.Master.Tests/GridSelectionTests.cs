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

    [Fact]
    public void Clear_TiraTudo_EAAncora()
    {
        var grid = Grid("A", "B", "C", "D");
        grid.Click("A");
        grid.CtrlClick("C");

        grid.Clear();

        Assert.Empty(grid.Selected);
        Assert.Null(grid.Anchor);
    }

    // The selection rectangle: while dragging, the tiles it touches.

    [Fact]
    public void Band_SemCtrl_SubstituiASelecao_NaOrdemDaGrade()
    {
        var grid = Grid("A", "B", "C", "D");
        grid.Click("A");

        grid.BeginBand();
        grid.Band(new[] { "C", "B" }, add: false);

        Assert.Equal(new[] { "B", "C" }, grid.Selected);
        Assert.Equal("B", grid.Anchor);
    }

    [Fact]
    public void Band_AcompanhaORetanguloEnquantoArrasta()
    {
        var grid = Grid("A", "B", "C", "D");

        grid.BeginBand();
        grid.Band(new[] { "A", "B", "C" }, add: false);
        grid.Band(new[] { "A" }, add: false);   // o retângulo encolheu

        Assert.Equal(new[] { "A" }, grid.Selected);
    }

    [Fact]
    public void Band_ComCtrl_SomaAoQueEstavaSelecionadoNoComeco()
    {
        var grid = Grid("A", "B", "C", "D");
        grid.Click("A");

        grid.BeginBand();
        grid.Band(new[] { "C" }, add: true);   // {A, C}
        grid.Band(new[] { "D" }, add: true);   // o retângulo saiu de C: {A, D}

        Assert.Equal(new[] { "A", "D" }, grid.Selected);
        Assert.Equal("D", grid.Anchor);
    }

    [Fact]
    public void Band_QueNaoTocaNada_SemCtrl_LimpaTudo()
    {
        var grid = Grid("A", "B", "C", "D");
        grid.Click("B");

        grid.BeginBand();
        grid.Band(Array.Empty<string>(), add: false);

        Assert.Empty(grid.Selected);
        Assert.Null(grid.Anchor);
    }

    [Fact]
    public void Band_QueNaoTocaNada_ComCtrl_VoltaAoQueEra()
    {
        var grid = Grid("A", "B", "C", "D");
        grid.Click("B");
        grid.CtrlClick("D");  // {B, D}, âncora D

        grid.BeginBand();
        grid.Band(new[] { "A" }, add: true);           // {A, B, D}
        grid.Band(Array.Empty<string>(), add: true);   // de volta a {B, D}

        Assert.Equal(new[] { "B", "D" }, grid.Selected);
        Assert.Equal("D", grid.Anchor);
    }

    [Fact]
    public void Band_SemBeginBand_ComCtrl_SoOQueTocou()
    {
        var grid = Grid("A", "B", "C", "D");

        grid.Band(new[] { "B" }, add: true);

        Assert.Equal(new[] { "B" }, grid.Selected);
    }

    [Fact]
    public void Band_IgnoraOQueNaoEstaNaGrade()
    {
        var grid = Grid("A", "B", "C", "D");

        grid.BeginBand();
        grid.Band(new[] { "X", "C" }, add: false);

        Assert.Equal(new[] { "C" }, grid.Selected);
        Assert.Equal("C", grid.Anchor);
    }

    [Fact]
    public void ShiftClick_DepoisDoRetangulo_ParteDaAncoraDele()
    {
        var grid = Grid("A", "B", "C", "D");
        grid.BeginBand();
        grid.Band(new[] { "B", "C" }, add: false);   // âncora B

        grid.ShiftClick("D");

        Assert.Equal(new[] { "B", "C", "D" }, grid.Selected);
    }

    // The right button and the tile's menu.

    [Fact]
    public void RightClick_NumaSelecaoDeVarias_MantemASelecao()
    {
        var grid = Grid("A", "B", "C", "D");
        grid.Click("A");
        grid.CtrlClick("C");  // {A, C}, âncora C

        grid.RightClick("A");

        Assert.Equal(new[] { "A", "C" }, grid.Selected);
        Assert.Equal("C", grid.Anchor);
    }

    [Fact]
    public void RightClick_ForaDaSelecao_SelecionaSoEla()
    {
        var grid = Grid("A", "B", "C", "D");
        grid.Click("A");
        grid.CtrlClick("C");

        grid.RightClick("B");

        Assert.Equal(new[] { "B" }, grid.Selected);
        Assert.Equal("B", grid.Anchor);
    }

    [Fact]
    public void RightClick_SemSelecao_SelecionaSoEla()
    {
        var grid = Grid("A", "B", "C", "D");

        grid.RightClick("D");

        Assert.Equal(new[] { "D" }, grid.Selected);
        Assert.Equal("D", grid.Anchor);
    }

    [Fact]
    public void TargetsOf_UmaDaSelecaoDeVarias_SaoTodasNaOrdemDaGrade()
    {
        var grid = Grid("A", "B", "C", "D");
        grid.Click("D");
        grid.CtrlClick("B");

        Assert.Equal(new[] { "B", "D" }, grid.TargetsOf("D"));
    }

    [Fact]
    public void TargetsOf_ForaDaSelecao_SoEla()
    {
        var grid = Grid("A", "B", "C", "D");
        grid.Click("D");
        grid.CtrlClick("B");

        Assert.Equal(new[] { "C" }, grid.TargetsOf("C"));
    }

    [Fact]
    public void TargetsOf_AUnicaSelecionada_SoEla()
    {
        var grid = Grid("A", "B", "C", "D");
        grid.Click("B");

        Assert.Equal(new[] { "B" }, grid.TargetsOf("B"));
    }
}
