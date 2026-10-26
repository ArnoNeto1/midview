using Midview.Master;
using Midview.Protocol;
using Xunit;

public partial class ClassFolderTextTests
{
    private const long KB = 1024;
    private const long MB = 1024 * KB;
    private const long GB = 1024 * MB;

    // --- The class list ---

    [Theory]
    [InlineData(38, "38 arquivos · última aula 14/10")]
    [InlineData(1, "1 arquivo · última aula 14/10")]
    [InlineData(1234, "1.234 arquivos · última aula 14/10")]
    public void Summary_CountsTheFilesAndSaysTheLastLesson(int files, string expected) =>
        Assert.Equal(expected, ClassFolderText.Summary(new ClassSummary("3º A", files, 10 * MB, new DateOnly(2026, 10, 14))));

    [Fact]
    public void Summary_OfAnEmptyClass_IsVazia() =>
        Assert.Equal("vazia", ClassFolderText.Summary(new ClassSummary("3º A", 0, 0, null)));

    // A class with files always has a last lesson; without one, just the count.
    [Fact]
    public void Summary_WithoutALastLesson_OnlyCounts() =>
        Assert.Equal("5 arquivos", ClassFolderText.Summary(new ClassSummary("3º A", 5, 10 * MB, null)));

    [Theory]
    [InlineData(38, "Excluir a turma 3º A? Os 38 arquivos dela vão para a Lixeira deste PC.")]
    [InlineData(1, "Excluir a turma 3º A? O arquivo dela vai para a Lixeira deste PC.")]
    [InlineData(0, "Excluir a turma 3º A? Ela está vazia.")]
    public void DeleteQuestion_SaysHowManyFilesGo(int files, string expected) =>
        Assert.Equal(expected, ClassFolderText.DeleteQuestion(new ClassSummary("3º A", files, files * KB, null), tooBigForTheBin: false));

    // More than this PC's Recycle Bin holds (RecycleBinRoom): Windows deletes them for good, and the question says so.
    [Theory]
    [InlineData(38, "Excluir a turma 3º A? Os 38 arquivos dela (38 KB) não cabem na Lixeira deste PC e serão apagados de vez.")]
    [InlineData(1, "Excluir a turma 3º A? O arquivo dela (1 KB) não cabe na Lixeira deste PC e será apagado de vez.")]
    [InlineData(0, "Excluir a turma 3º A? Ela está vazia.")]
    public void DeleteQuestion_TooBigForTheRecycleBin_SaysTheyGoForGood(int files, string expected) =>
        Assert.Equal(expected, ClassFolderText.DeleteQuestion(new ClassSummary("3º A", files, files * KB, null), tooBigForTheBin: true));

    [Theory]
    [InlineData("3º A", "Começar a aula do 3º A")]
    [InlineData(null, "Começar a aula")]
    public void StartButton_NamesTheChosenClass(string? turma, string expected) =>
        Assert.Equal(expected, ClassFolderText.StartButton(turma));

    // --- Sizes, in the teacher's own number format ---

    [Theory]
    [InlineData(500L, "500 bytes")]
    [InlineData(12 * KB, "12 KB")]
    [InlineData(1536 * KB, "1,5 MB")]
    [InlineData(200 * MB, "200 MB")]
    [InlineData(5 * GB, "5 GB")]
    [InlineData(180 * GB, "180 GB")]
    public void Size_IsShortAndInPortuguese(long bytes, string expected) =>
        Assert.Equal(expected, ClassFolderText.Size(bytes));

    [Fact]
    public void Size_KeepsOneDecimalUnderTen() =>
        Assert.Equal("1,2 GB", ClassFolderText.Size((long)(1.2 * GB)));

    // --- During a lesson ---

    [Fact]
    public void Header_SaysWhenItStartedTheFilesAndTheFreeSpace() =>
        Assert.Equal(
            "Aula começou às 13:30 · 214 arquivos (1,2 GB) · disco deste PC com 180 GB livres",
            ClassFolderText.Header(LessonStartedAt(13, 30), 214, (long)(1.2 * GB), 180 * GB));

    [Fact]
    public void Header_OneFile() =>
        Assert.Equal(
            "Aula começou às 08:05 · 1 arquivo (12 KB) · disco deste PC com 180 GB livres",
            ClassFolderText.Header(LessonStartedAt(8, 5), 1, 12 * KB, 180 * GB));

    [Fact]
    public void Header_NoFilesYet() =>
        Assert.Equal(
            "Aula começou às 13:30 · nenhum arquivo ainda · disco deste PC com 180 GB livres",
            ClassFolderText.Header(LessonStartedAt(13, 30), 0, 0, 180 * GB));

    // ClassStore.FreeBytes says long.MaxValue when the free space can't be told.
    [Fact]
    public void Header_FreeSpaceUnknown_LeavesItOut() =>
        Assert.Equal(
            "Aula começou às 13:30 · 214 arquivos (1,2 GB)",
            ClassFolderText.Header(LessonStartedAt(13, 30), 214, (long)(1.2 * GB), long.MaxValue));

    [Fact]
    public void LowSpaceWarning_UnderTheLimit_SaysHowMuchIsLeft() =>
        Assert.Equal(
            "O disco deste computador está ficando cheio (3,2 GB livres). Apague turmas antigas para continuar guardando arquivos.",
            ClassFolderText.LowSpaceWarning((long)(3.2 * GB)));

    [Theory]
    [InlineData(ClassStore.LowSpaceBytes)]
    [InlineData(180 * GB)]
    [InlineData(long.MaxValue)]
    public void LowSpaceWarning_EnoughSpace_IsNull(long freeBytes) =>
        Assert.Null(ClassFolderText.LowSpaceWarning(freeBytes));

    [Theory]
    [InlineData("LAB-01", PcSyncState.InSync, "", "✓ LAB-01")]
    [InlineData("LAB-03", PcSyncState.Copying, "", "⟳ LAB-03")]
    [InlineData("LAB-03", PcSyncState.Copying, "copiando 12 arquivos", "⟳ LAB-03")]
    [InlineData("LAB-06", PcSyncState.Offline, "", "○ LAB-06")]
    [InlineData("LAB-09", PcSyncState.Failed, "Midview antigo", "✗ LAB-09: Midview antigo")]
    [InlineData("LAB-09", PcSyncState.Failed, "", "✗ LAB-09")]
    public void State_IsAMarkAndTheName_WithTheReasonOfAFailure(string name, PcSyncState state, string detail, string expected) =>
        Assert.Equal(expected, ClassFolderText.State(Status(name, state, detail)));

    [Theory]
    [InlineData(PcSyncState.InSync, "", "Em dia")]
    [InlineData(PcSyncState.Copying, "copiando 12 arquivos", "Copiando: copiando 12 arquivos")]
    [InlineData(PcSyncState.Copying, "", "Copiando")]
    [InlineData(PcSyncState.Offline, "", "Desligado: recebe ao ligar")]
    [InlineData(PcSyncState.Failed, "Midview antigo", "Midview antigo")]
    public void StateHint_SaysWhatTheMarkMeans(PcSyncState state, string detail, string expected) =>
        Assert.Equal(expected, ClassFolderText.StateHint(Status("LAB-01", state, detail)));

    // --- Ending a lesson ---

    [Theory]
    [InlineData(EndChoice.Keep, 23, 214, "Voltam para todos os PCs na próxima aula do 3º A.")]
    [InlineData(EndChoice.DeleteToday, 23, 214, "Os 23 arquivos desta aula saem; os das aulas anteriores ficam.")]
    [InlineData(EndChoice.DeleteToday, 1, 214, "O arquivo desta aula sai; os das aulas anteriores ficam.")]
    [InlineData(EndChoice.DeleteToday, 0, 214, "Nenhum arquivo chegou nesta aula; os das aulas anteriores ficam.")]
    [InlineData(EndChoice.DeleteAll, 23, 214, "Os 214 arquivos saem do seu PC e de todos os PCs. Vão para a Lixeira deste PC.")]
    [InlineData(EndChoice.DeleteAll, 1, 1, "O arquivo sai do seu PC e de todos os PCs. Vai para a Lixeira deste PC.")]
    [InlineData(EndChoice.DeleteAll, 0, 0, "Não há arquivos na pasta da turma.")]
    public void EndChoiceHint_SaysWhatHappensToTheFiles(EndChoice choice, int todayFiles, int allFiles, string expected) =>
        Assert.Equal(expected, ClassFolderText.EndChoiceHint(choice, "3º A", todayFiles, allFiles, tooBigForTheBin: false));

    [Theory]
    [InlineData(EndChoice.Keep, 23, 214, "Voltam para todos os PCs na próxima aula do 3º A.")]
    [InlineData(EndChoice.DeleteToday, 23, 214, "Os 23 arquivos desta aula saem; os das aulas anteriores ficam. Os desta aula não cabem na Lixeira deste PC: serão apagados de vez.")]
    [InlineData(EndChoice.DeleteToday, 1, 214, "O arquivo desta aula sai; os das aulas anteriores ficam. Ele não cabe na Lixeira deste PC: será apagado de vez.")]
    [InlineData(EndChoice.DeleteToday, 0, 214, "Nenhum arquivo chegou nesta aula; os das aulas anteriores ficam.")]
    [InlineData(EndChoice.DeleteAll, 23, 214, "Os 214 arquivos saem do seu PC e de todos os PCs. Não cabem na Lixeira deste PC: serão apagados de vez.")]
    [InlineData(EndChoice.DeleteAll, 1, 1, "O arquivo sai do seu PC e de todos os PCs. Não cabe na Lixeira deste PC: será apagado de vez.")]
    [InlineData(EndChoice.DeleteAll, 0, 0, "Não há arquivos na pasta da turma.")]
    public void EndChoiceHint_TooBigForTheRecycleBin_SaysTheyGoForGood(EndChoice choice, int todayFiles, int allFiles, string expected) =>
        Assert.Equal(expected, ClassFolderText.EndChoiceHint(choice, "3º A", todayFiles, allFiles, tooBigForTheBin: true));

    [Theory]
    [InlineData(EndChoice.DeleteToday, "Encerrar a aula do 3º A e apagar os arquivos de hoje?\n\nOs 23 arquivos desta aula saem; os das aulas anteriores ficam.")]
    [InlineData(EndChoice.DeleteAll, "Encerrar a aula do 3º A e apagar tudo do 3º A?\n\nOs 214 arquivos saem do seu PC e de todos os PCs. Vão para a Lixeira deste PC.")]
    public void EndQuestion_AsksBeforeDeleting(EndChoice choice, string expected) =>
        Assert.Equal(expected, ClassFolderText.EndQuestion(choice, "3º A", 23, 214, tooBigForTheBin: false));

    [Fact]
    public void EndQuestion_TooBigForTheRecycleBin_DoesNotPromiseIt() =>
        Assert.Equal(
            "Encerrar a aula do 3º A e apagar tudo do 3º A?\n\nOs 214 arquivos saem do seu PC e de todos os PCs. Não cabem na Lixeira deste PC: serão apagados de vez.",
            ClassFolderText.EndQuestion(EndChoice.DeleteAll, "3º A", 23, 214, tooBigForTheBin: true));

    [Fact]
    public void EndQuestion_KeepingTheFiles_AsksNothing() =>
        Assert.Null(ClassFolderText.EndQuestion(EndChoice.Keep, "3º A", 23, 214, tooBigForTheBin: true));

    // --- What each PC answered ---

    [Theory]
    [InlineData("LAB-01", true, "aula do 3º A", "✓ LAB-01: aula do 3º A")]
    [InlineData("LAB-09", false, "Midview antigo", "✗ LAB-09: Midview antigo")]
    [InlineData("LAB-01", true, "", "✓ LAB-01")]
    public void Result_IsOneLinePerPc(string name, bool ok, string detail, string expected) =>
        Assert.Equal(expected, ClassFolderText.Result(name, ok, detail));

    // offline: how many of the PCs that didn't take it were switched off. Only
    // then "they get it when they're turned on"; any other reason is in the PC's ✗ line.
    [Theory]
    [InlineData(true, 3, 3, 0, "A aula do 3º A começou em todos os 3 PCs.")]
    [InlineData(true, 2, 3, 1, "A aula do 3º A começou em 2 de 3 PCs; os desligados recebem a pasta ao ligar.")]
    [InlineData(true, 2, 3, 0, "A aula do 3º A começou em 2 de 3 PCs.")]
    [InlineData(true, 1, 3, 1, "A aula do 3º A começou em 1 de 3 PCs.")]
    [InlineData(true, 0, 0, 0, "A aula do 3º A começou.")]
    [InlineData(true, 1, 1, 0, "A aula do 3º A começou no único PC da sala.")]
    [InlineData(true, 0, 1, 1, "A aula do 3º A começou em 0 de 1 PC; os desligados recebem a pasta ao ligar.")]
    [InlineData(false, 3, 3, 0, "A aula do 3º A foi encerrada em todos os 3 PCs.")]
    [InlineData(false, 1, 3, 2, "A aula do 3º A foi encerrada em 1 de 3 PCs; os desligados encerram ao ligar.")]
    [InlineData(false, 1, 3, 0, "A aula do 3º A foi encerrada em 1 de 3 PCs.")]
    [InlineData(false, 0, 0, 0, "A aula do 3º A foi encerrada.")]
    public void Outcome_CountsThePcsThatTookIt(bool started, int reached, int total, int offline, string expected) =>
        Assert.Equal(expected, ClassFolderText.Outcome(started, "3º A", reached, total, offline));

    // --- The class folders on another teacher PC ---

    [Fact]
    public void RemoteBanner_SaysWhereTheFoldersAre() =>
        Assert.Equal(
            "As pastas das turmas ficam no PC PROF-1. O que você fizer aqui acontece nele.", ClassFolderText.RemoteBanner("PROF-1"));

    [Fact]
    public void HomeClosed_NamesTheHomePc() =>
        Assert.Equal("O Midview do PC PROF-1, que guarda as pastas das turmas, está fechado.", ClassFolderText.HomeClosed("PROF-1"));

    [Fact]
    public void HomeClosedHint_PointsAtTheFirewallRule() =>
        Assert.Equal(
            "Se ele está aberto, o Midview desse PC precisa ser instalado de novo para aceitar este computador (regra do firewall).",
            ClassFolderText.HomeClosedHint);

    [Fact]
    public void HomeNoAnswer_NamesTheHomePc() =>
        Assert.Equal("O Midview do PC PROF-1 não respondeu. Tente de novo em instantes.", ClassFolderText.HomeNoAnswer("PROF-1"));

    [Fact]
    public void HomeOtherIdentity_AsksWhetherItWasReinstalled() =>
        Assert.Equal("O PC PROF-1 respondeu com outra identidade; ele foi reinstalado?", ClassFolderText.HomeOtherIdentity("PROF-1"));

    // On another teacher PC, the disk and the Recycle Bin are the home PC's: the texts name it.
    [Fact]
    public void FromAnotherTeacherPc_TheHomesDiskAndRecycleBinAreNamed()
    {
        const string Home = "PROF-1";
        var summary = new ClassSummary("3º A", 38, 38 * KB, null);
        var lesson = LessonStartedAt(13, 30);

        Assert.Equal(
            "Excluir a turma 3º A? Os 38 arquivos dela vão para a Lixeira do PC PROF-1.",
            ClassFolderText.DeleteQuestion(summary, tooBigForTheBin: false, Home));
        Assert.Equal(
            "Excluir a turma 3º A? Os 38 arquivos dela (38 KB) não cabem na Lixeira do PC PROF-1 e serão apagados de vez.",
            ClassFolderText.DeleteQuestion(summary, tooBigForTheBin: true, Home));
        Assert.Equal(
            "Turma 3º A excluída. Os 38 arquivos dela estão na Lixeira do PC PROF-1.",
            ClassFolderText.Deleted(summary, tooBigForTheBin: false, Home));
        Assert.Equal(
            "Turma 3º A excluída. O arquivo dela foi apagado de vez: não cabia na Lixeira do PC PROF-1.",
            ClassFolderText.Deleted(new ClassSummary("3º A", 1, KB, null), tooBigForTheBin: true, Home));
        Assert.EndsWith("· disco do PC PROF-1 com 180 GB livres", ClassFolderText.Header(lesson, 214, GB, 180 * GB, Home));
        Assert.Equal(
            "O disco do PC PROF-1 está ficando cheio (1 GB livres). Apague turmas antigas para continuar guardando arquivos.",
            ClassFolderText.LowSpaceWarning(GB, Home));
        Assert.Equal(
            "Os 3 arquivos desta aula saem; os das aulas anteriores ficam. Os desta aula não cabem na Lixeira do PC PROF-1: serão apagados de vez.",
            ClassFolderText.EndChoiceHint(EndChoice.DeleteToday, "3º A", 3, 10, tooBigForTheBin: true, Home));
        Assert.Equal(
            "Os 10 arquivos saem do PC PROF-1 e de todos os PCs. Vão para a Lixeira do PC PROF-1.",
            ClassFolderText.EndChoiceHint(EndChoice.DeleteAll, "3º A", 3, 10, tooBigForTheBin: false, Home));
        Assert.Equal(
            "Encerrar a aula do 3º A e apagar tudo do 3º A?\n\nO arquivo sai do PC PROF-1 e de todos os PCs. Não cabe na Lixeira do PC PROF-1: será apagado de vez.",
            ClassFolderText.EndQuestion(EndChoice.DeleteAll, "3º A", 1, 1, tooBigForTheBin: true, Home));
        Assert.Equal(
            "A pasta da turma do PC PROF-1 não pôde ser lida agora. Tente de novo em instantes.",
            ClassFolderText.LessonFilesUnreadable(Home));
    }

    // On the home itself, as before.
    [Fact]
    public void OnTheHome_ItsOwnDiskAndRecycleBinAreThisPcs()
    {
        Assert.Equal(
            "Os 10 arquivos saem do seu PC e de todos os PCs. Vão para a Lixeira deste PC.",
            ClassFolderText.EndChoiceHint(EndChoice.DeleteAll, "3º A", 3, 10, tooBigForTheBin: false));
        Assert.Equal(
            "A pasta da turma deste PC não pôde ser lida agora. Tente de novo em instantes.", ClassFolderText.LessonFilesUnreadable());
        Assert.StartsWith("O disco deste computador está ficando cheio", ClassFolderText.LowSpaceWarning(GB));
    }

    // Ended straight on the PCs with the home's Midview closed: nothing hands
    // the end to the PCs that were off until it opens, or the day ends.
    [Theory]
    [InlineData(2, 3, 1, "A aula do 3º A foi encerrada em 2 de 3 PCs; os desligados continuam na aula até o Midview do PC PROF-1 abrir ou o dia acabar.")]
    [InlineData(3, 3, 0, "A aula do 3º A foi encerrada em todos os 3 PCs.")]
    [InlineData(1, 3, 1, "A aula do 3º A foi encerrada em 1 de 3 PCs.")]
    public void Outcome_OfAnEndWithTheHomeClosed_DoesNotPromiseTheOffPcs(int reached, int total, int offline, string expected) =>
        Assert.Equal(expected, ClassFolderText.Outcome(started: false, "3º A", reached, total, offline, closedHome: "PROF-1"));

    [Fact]
    public void AnEndNoPcAnswered_SaysSo() =>
        Assert.Equal(
            "Não foi possível encerrar a aula do 3º A: nenhum PC da sala respondeu",
            ClassFolderText.EndFailed("3º A", ClassFolderText.NoPcAnswered));

    // --- Changes to the list ---

    [Fact]
    public void Renamed_SaysTheNewName() =>
        Assert.Equal("Turma 3º A agora se chama 3º C.", ClassFolderText.Renamed("3º A", "3º C"));

    [Theory]
    [InlineData(38, "Turma 3º A excluída. Os 38 arquivos dela estão na Lixeira deste PC.")]
    [InlineData(1, "Turma 3º A excluída. O arquivo dela está na Lixeira deste PC.")]
    [InlineData(0, "Turma 3º A excluída.")]
    public void Deleted_MentionsTheRecycleBinOnlyWhenFilesWentThere(int files, string expected) =>
        Assert.Equal(expected, ClassFolderText.Deleted(new ClassSummary("3º A", files, files * KB, null), tooBigForTheBin: false));

    [Theory]
    [InlineData(38, "Turma 3º A excluída. Os 38 arquivos dela foram apagados de vez: não cabiam na Lixeira deste PC.")]
    [InlineData(1, "Turma 3º A excluída. O arquivo dela foi apagado de vez: não cabia na Lixeira deste PC.")]
    [InlineData(0, "Turma 3º A excluída.")]
    public void Deleted_TooBigForTheRecycleBin_SaysTheyWentForGood(int files, string expected) =>
        Assert.Equal(expected, ClassFolderText.Deleted(new ClassSummary("3º A", files, files * KB, null), tooBigForTheBin: true));

    private static ClassLesson LessonStartedAt(int hour, int minute)
    {
        var start = new DateTimeOffset(new DateTime(2026, 10, 20, hour, minute, 0, DateTimeKind.Local)).ToUnixTimeSeconds();
        return new ClassLesson(start, Guid.NewGuid(), "3º A", start, Ended: false);
    }

    private static PcStatus Status(string name, PcSyncState state, string detail) =>
        new(new KnownMachine(name, "127.0.0.2", "AB", "CD"), state, detail);
}
