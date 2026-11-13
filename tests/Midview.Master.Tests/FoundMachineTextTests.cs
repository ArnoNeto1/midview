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

    // Only another teacher's machine taking one more asks for the code.
    [Fact]
    public void NeedsNewTeacherCode_OnlyForAnotherTeachersOpenMachine()
    {
        var none = Array.Empty<KnownMachine>();
        Assert.True(FoundMachineText.NeedsNewTeacherCode(Found(TeacherStatus.Others, true), none));
        Assert.False(FoundMachineText.NeedsNewTeacherCode(Found(TeacherStatus.Others), none));
        Assert.False(FoundMachineText.NeedsNewTeacherCode(Found(TeacherStatus.None, true), none));
        Assert.False(FoundMachineText.NeedsNewTeacherCode(Found(TeacherStatus.You, true), none));
        var roster = new[] { new KnownMachine("Aluno-2", "10.0.0.2", new string('5', 40), X) };
        Assert.False(FoundMachineText.NeedsNewTeacherCode(Found(TeacherStatus.Others, true), roster));
    }
}
