using Midview.Master;
using Midview.Protocol;
using Xunit;

public class FoundMachineTextTests
{
    private static readonly string X = new('A', 40);

    private static FoundMachine Found(string teachers, bool open = false) => new("Aluno-2", "10.0.0.2", X, teachers, open);

    [Theory]
    [InlineData(TeacherStatus.None, false, "Sem professor")]
    [InlineData(TeacherStatus.You, false, "Pareada com você")]
    [InlineData(TeacherStatus.Others, false, "Com outro professor")]
    [InlineData(TeacherStatus.Others, true, "Com outro professor — aceitando novo professor")]
    public void Status_SaysWhoseMachineItIs(string teachers, bool open, string expected)
    {
        Assert.Equal(expected, FoundMachineText.Status(Found(teachers, open), Array.Empty<KnownMachine>()));
    }

    [Fact]
    public void AMachineAlreadyInTheRoster_SaysSo_AndIsNotChecked()
    {
        var roster = new[] { new KnownMachine("Aluno-2", "10.0.0.2", new string('5', 40), X.ToLowerInvariant()) };

        Assert.Equal("Já está na sala", FoundMachineText.Status(Found(TeacherStatus.You), roster));
        Assert.False(FoundMachineText.CheckedByDefault(Found(TeacherStatus.None), roster));
    }

    [Fact]
    public void OnlyMachinesWithNoTeacher_AreCheckedByDefault()
    {
        Assert.True(FoundMachineText.CheckedByDefault(Found(TeacherStatus.None), Array.Empty<KnownMachine>()));
        Assert.False(FoundMachineText.CheckedByDefault(Found(TeacherStatus.You), Array.Empty<KnownMachine>()));
        Assert.False(FoundMachineText.CheckedByDefault(Found(TeacherStatus.Others, true), Array.Empty<KnownMachine>()));
    }

    [Theory]
    [InlineData(0, 0, "Nenhuma máquina respondeu. Confira se elas estão ligadas, com o Midview Aluno instalado e nesta mesma rede.")]
    [InlineData(1, 0, "1 máquina encontrada.")]
    [InlineData(25, 0, "25 máquinas encontradas.")]
    [InlineData(25, 1, "25 máquinas encontradas. 1 endereço atualizado na lista da sala.")]
    [InlineData(25, 3, "25 máquinas encontradas. 3 endereços atualizados na lista da sala.")]
    public void SearchSummary(int found, int refreshed, string expected)
    {
        Assert.Equal(expected, FoundMachineText.SearchSummary(found, refreshed));
    }

    [Fact]
    public void AllowNewTeacherSummary_CountsAndNamesTheOnesLeftOut()
    {
        Assert.Equal("A lista da sala está vazia.", FoundMachineText.AllowNewTeacherSummary(Array.Empty<(string, bool, string)>()));
        Assert.Equal(
            "2 de 2 máquinas liberadas por 10 minutos.",
            FoundMachineText.AllowNewTeacherSummary(new[] { ("Aluno-1", true, "liberada até 14:32"), ("Aluno-2", true, "liberada até 14:32") }));
        Assert.Equal(
            "1 de 3 máquinas liberadas por 10 minutos. Não liberadas: Aluno-2 (Sem resposta); Aluno-3 (Midview antigo: atualize-a).",
            FoundMachineText.AllowNewTeacherSummary(new[]
            {
                ("Aluno-1", true, "liberada até 14:32"),
                ("Aluno-2", false, "Sem resposta"),
                ("Aluno-3", false, "Midview antigo: atualize-a"),
            }));
    }
}
