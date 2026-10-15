using System.Windows;
using Midview.Master;
using Xunit;

// Which tiles the selection rectangle touches, on tile bounds laid out like
// the grid: 100x80 cards, 20 apart.
public sealed class RubberBandTests
{
    private static readonly (string Item, Rect Bounds)[] TwoByTwo =
    {
        ("A", new Rect(0, 0, 100, 80)), ("B", new Rect(120, 0, 100, 80)),
        ("C", new Rect(0, 100, 100, 80)), ("D", new Rect(120, 100, 100, 80)),
    };

    [Fact]
    public void Touched_PegaOQueORetanguloEncostaMesmoEmParte()
    {
        var band = RubberBand.Between(new Point(90, 70), new Point(130, 75));

        Assert.Equal(new[] { "A", "B" }, RubberBand.Touched(band, TwoByTwo));
    }

    [Fact]
    public void Touched_NoVaoEntreAsMaquinas_Nada()
    {
        var band = RubberBand.Between(new Point(102, 82), new Point(118, 98));

        Assert.Empty(RubberBand.Touched(band, TwoByTwo));
    }

    [Fact]
    public void Touched_PeloMeio_PegaAsQuatro_NaOrdemDaGrade()
    {
        var band = RubberBand.Between(new Point(90, 70), new Point(130, 110));

        Assert.Equal(new[] { "A", "B", "C", "D" }, RubberBand.Touched(band, TwoByTwo));
    }

    [Fact]
    public void Touched_ArrastandoDeBaixoParaCima_PegaOMesmo()
    {
        var down = RubberBand.Between(new Point(50, 40), new Point(150, 140));
        var up = RubberBand.Between(new Point(150, 140), new Point(50, 40));

        Assert.Equal(new[] { "A", "B", "C", "D" }, RubberBand.Touched(down, TwoByTwo));
        Assert.Equal(new[] { "A", "B", "C", "D" }, RubberBand.Touched(up, TwoByTwo));
    }

    // A rectangle dragged straight across has no height, and still crosses tiles.
    [Fact]
    public void Touched_RetanguloSemAltura_PegaAsQueCruza()
    {
        var band = RubberBand.Between(new Point(10, 120), new Point(200, 120));

        Assert.Equal(new[] { "C", "D" }, RubberBand.Touched(band, TwoByTwo));
    }

    // A tile that isn't laid out (no bounds) can't be touched.
    [Fact]
    public void Touched_IgnoraMaquinaSemPosicao()
    {
        var band = RubberBand.Between(new Point(0, 0), new Point(500, 500));

        Assert.Equal(new[] { "A" }, RubberBand.Touched(band, new[] { ("A", new Rect(0, 0, 100, 80)), ("B", Rect.Empty) }));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(3, 4, false)]
    [InlineData(-4, 4, false)]
    [InlineData(5, 0, true)]
    [InlineData(0, -5, true)]
    public void IsDrag_SoDepoisDeAndarMaisQueUnsPixels(double dx, double dy, bool expected)
    {
        Assert.Equal(expected, RubberBand.IsDrag(new Point(100, 100), new Point(100 + dx, 100 + dy)));
    }
}
