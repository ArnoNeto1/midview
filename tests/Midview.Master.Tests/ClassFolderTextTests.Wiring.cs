using Midview.Master;
using Xunit;

// What the Master says around the two teacher PCs' wiring (Task 7).
public partial class ClassFolderTextTests
{
    private const string Where = @"C:\Users\prof\Documents\Midview";

    [Fact]
    public void AsideNote_ForOneFolder_SaysWhereItIs() => Assert.Equal(
        "Há turmas guardadas à parte na pasta \"Turmas (antes de 20-10-2026 1330)\", em C:\\Users\\prof\\Documents\\Midview. "
        + "Esta janela não mostra essas turmas: abra a pasta no Explorador de Arquivos para ver ou copiar os arquivos.",
        ClassFolderText.AsideNote(new[] { "Turmas (antes de 20-10-2026 1330)" }, Where));

    [Theory]
    [InlineData(2, "\"T1\" e \"T2\"")]
    [InlineData(3, "\"T1\", \"T2\" e \"T3\"")]
    [InlineData(5, "\"T1\", \"T2\", \"T3\" e mais 2")]
    public void AsideNote_ForSeveralFolders_NamesThreeAtMost(int count, string named) => Assert.Equal(
        $"Há turmas guardadas à parte nas pastas {named}, em C:\\Users\\prof\\Documents\\Midview. "
        + "Esta janela não mostra essas turmas: abra as pastas no Explorador de Arquivos para ver ou copiar os arquivos.",
        ClassFolderText.AsideNote(Enumerable.Range(1, count).Select(i => $"T{i}").ToList(), Where));

    [Fact]
    public void AsideNote_ForNoFolder_IsNone() => Assert.Null(ClassFolderText.AsideNote(Array.Empty<string>(), Where));

    // On a PC working another PC's folders whose own "Turmas" holds classes.
    [Fact]
    public void AsideNote_ForThisPcsOwnClasses_SaysTheyStayInTurmas() => Assert.Equal(
        "As turmas deste computador continuam na pasta \"Turmas\", em C:\\Users\\prof\\Documents\\Midview. "
        + "Esta janela não mostra essas turmas: abra a pasta no Explorador de Arquivos para ver ou copiar os arquivos.",
        ClassFolderText.AsideNote(Array.Empty<string>(), Where, ownClassesHidden: true));

    [Fact]
    public void AsideNote_ForAnAsideAndThisPcsOwnClasses_SaysBoth() => Assert.Equal(
        "Há turmas guardadas à parte na pasta \"Turmas (antes de 20-10-2026 1330)\", em C:\\Users\\prof\\Documents\\Midview. "
        + "As turmas deste computador continuam na pasta \"Turmas\", ao lado dela. "
        + "Esta janela não mostra essas turmas: abra as pastas no Explorador de Arquivos para ver ou copiar os arquivos.",
        ClassFolderText.AsideNote(new[] { "Turmas (antes de 20-10-2026 1330)" }, Where, ownClassesHidden: true));

    [Fact]
    public void HandOverStillRunning_IsTheAgreedText() => Assert.Equal(
        "As pastas das turmas estão passando de um computador para outro. Espere a troca terminar para fechar o Midview.",
        ClassFolderText.HandOverStillRunning);

    // The home's full disk, said of the home: only when the line ends there or
    // with a round's count of more files (" e mais 2"), nothing else after it.
    [Theory]
    [InlineData("a.docx (não há espaço livre suficiente neste computador)", "a.docx (não há espaço livre suficiente no PC PROF-1)")]
    [InlineData("a.docx (não há espaço livre suficiente neste computador) e mais 2", "a.docx (não há espaço livre suficiente no PC PROF-1) e mais 2")]
    [InlineData("a.docx (não há espaço livre suficiente neste computador) e mais 12", "a.docx (não há espaço livre suficiente no PC PROF-1) e mais 12")]
    [InlineData("a.docx (não há espaço livre suficiente neste computador) e mais tarde", "a.docx (não há espaço livre suficiente neste computador) e mais tarde")]
    [InlineData("a.docx (não há espaço livre suficiente neste computador) e mais 2 b", "a.docx (não há espaço livre suficiente neste computador) e mais 2 b")]
    [InlineData("a.docx (não há espaço livre suficiente neste computador) e mais ", "a.docx (não há espaço livre suficiente neste computador) e mais ")]
    public void Relayed_SaysTheFullDiskOfTheHome_OnlyBeforeACount(string detail, string expected) =>
        Assert.Equal(expected, ClassFolderText.Relayed(detail, "PROF-1"));
}
