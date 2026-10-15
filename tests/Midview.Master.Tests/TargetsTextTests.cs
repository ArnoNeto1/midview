using Midview.Master;
using Xunit;

// The line at the top of each action window saying which machines it acts on.
public sealed class TargetsTextTests
{
    [Theory]
    [InlineData(TargetsPreposition.In, "Em: ALUNO-01")]
    [InlineData(TargetsPreposition.From, "De: ALUNO-01")]
    [InlineData(TargetsPreposition.To, "Para: ALUNO-01")]
    public void UmaMaquina_ComoAntes(TargetsPreposition preposition, string expected)
    {
        Assert.Equal(expected, TargetsText.Describe(preposition, Names(1), wholeClass: false));
    }

    [Theory]
    [InlineData(TargetsPreposition.In, "Na turma toda (24 máquinas)")]
    [InlineData(TargetsPreposition.From, "Da turma toda (24 máquinas)")]
    [InlineData(TargetsPreposition.To, "Para a turma toda (24 máquinas)")]
    public void ATurmaToda_ComoAntes(TargetsPreposition preposition, string expected)
    {
        Assert.Equal(expected, TargetsText.Describe(preposition, Names(24), wholeClass: true));
    }

    // A class of one is still one machine.
    [Fact]
    public void TurmaDeUmaSo_DizAMaquina()
    {
        Assert.Equal("Em: ALUNO-01", TargetsText.Describe(TargetsPreposition.In, Names(1), wholeClass: true));
    }

    [Theory]
    [InlineData(TargetsPreposition.In, "Em 3 máquinas: ALUNO-01, ALUNO-02, ALUNO-03")]
    [InlineData(TargetsPreposition.From, "De 3 máquinas: ALUNO-01, ALUNO-02, ALUNO-03")]
    [InlineData(TargetsPreposition.To, "Para 3 máquinas: ALUNO-01, ALUNO-02, ALUNO-03")]
    public void ParteDaTurma_NaoDizTurmaToda_EDizQuais(TargetsPreposition preposition, string expected)
    {
        Assert.Equal(expected, TargetsText.Describe(preposition, Names(3), wholeClass: false));
    }

    [Fact]
    public void ParteDaTurma_CincoMaquinas_DizTodas()
    {
        Assert.Equal(
            "Em 5 máquinas: ALUNO-01, ALUNO-02, ALUNO-03, ALUNO-04, ALUNO-05",
            TargetsText.Describe(TargetsPreposition.In, Names(5), wholeClass: false));
    }

    [Fact]
    public void ParteDaTurma_MaisDeCinco_DizAsCincoPrimeiras_EContaOResto()
    {
        Assert.Equal(
            "Para 8 máquinas: ALUNO-01, ALUNO-02, ALUNO-03, ALUNO-04, ALUNO-05 e mais 3",
            TargetsText.Describe(TargetsPreposition.To, Names(8), wholeClass: false));
    }

    private static string[] Names(int count) => Enumerable.Range(1, count).Select(n => $"ALUNO-{n:00}").ToArray();
}
