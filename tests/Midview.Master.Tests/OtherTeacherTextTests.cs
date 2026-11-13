using Midview.Master;
using Midview.Protocol;
using Xunit;

public class OtherTeacherTextTests
{
    private const string WithCode = "liberada até 14:32 com o código";

    [Fact]
    public void AllowNewTeacherSummary_CountsAndNamesTheOnesLeftOut()
    {
        Assert.Equal("A lista da sala está vazia.", OtherTeacherText.AllowNewTeacherSummary(Array.Empty<(string, bool, string)>()));
        Assert.Equal(
            "2 de 2 máquinas liberadas por 10 minutos.",
            OtherTeacherText.AllowNewTeacherSummary(new[] { ("Aluno-1", true, WithCode), ("Aluno-2", true, WithCode) }));
        Assert.Equal(
            "1 de 3 máquinas liberadas por 10 minutos. Não liberadas: Aluno-2 (Sem resposta); Aluno-3 (Midview antigo: atualize-a).",
            OtherTeacherText.AllowNewTeacherSummary(new[]
            {
                ("Aluno-1", true, WithCode),
                ("Aluno-2", false, "Sem resposta"),
                ("Aluno-3", false, "Midview antigo: atualize-a"),
            }));
    }

    // An Agent from before the code opens the old window: anyone gets in.
    [Fact]
    public void AllowNewTeacherSummary_WarnsAboutMachinesThatTakeAnyone()
    {
        Assert.Equal(
            "2 de 2 máquinas liberadas por 10 minutos. Com Midview antigo, aceitam qualquer professor até lá, sem o código: Aluno-2. Atualize-as.",
            OtherTeacherText.AllowNewTeacherSummary(new[] { ("Aluno-1", true, WithCode), ("Aluno-2", true, "liberada até 14:32") }));
    }

    [Theory]
    [InlineData(false, "", OtherTeacherText.OldAgent)]
    [InlineData(false, PhoneAgentCommand.NotOnPhone, OtherTeacherText.OldAgent)]
    [InlineData(false, "Sem resposta", "Sem resposta")]
    [InlineData(true, "", "")]
    public void Detail_NamesAnOldAgent(bool succeeded, string detail, string expected) =>
        Assert.Equal(expected, OtherTeacherText.Detail(succeeded, detail));

    [Fact]
    public void ForgetSummary_SaysWhatEachMachineForgot()
    {
        Assert.Equal("A lista da sala está vazia.", OtherTeacherText.ForgetSummary(Array.Empty<(string, bool, string)>()));
        Assert.Equal(
            "2 de 3 máquinas agora só aceitam você. Esquecidos: Aluno-1 (2 professores esquecidos). Não alteradas: Aluno-3 (Midview antigo: atualize-a).",
            OtherTeacherText.ForgetSummary(new[]
            {
                ("Aluno-1", true, OtherTeachers.ForgottenText(2)),
                ("Aluno-2", true, OtherTeachers.ForgottenText(0)),
                ("Aluno-3", false, OtherTeacherText.OldAgent),
            }));
        Assert.Equal(
            "1 de 1 máquinas agora só aceitam você.",
            OtherTeacherText.ForgetSummary(new[] { ("Aluno-1", true, OtherTeachers.NoneForgotten) }));
    }

    [Fact]
    public void ForgetSummary_NamesTheMachinesLeftAsTheyWere() =>
        Assert.EndsWith(
            " Não alteradas: Aluno-3 (Sem resposta).",
            OtherTeacherText.ForgetSummary(new[] { ("Aluno-1", true, "1 professor esquecido"), ("Aluno-3", false, "Sem resposta") }));
}
