using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver when the copy can't all go in place, or can't all go
// back (Ruling 19 (b)): the result says where things are, never "nada mudou"
// unless all of it went back. The store's moves fail where the test says
// (ClassStore.MoveHook), as a file open in another program would make them.
public sealed partial class ClassFolderHandOverTests
{
    private const string OwnAside = "Turmas (antes de 20-10-2026 1330)";

    // The copy can't go in place, and this PC's own classes can't come back
    // from aside: the text says where they are.
    [Fact]
    public async Task ASwapThatFails_AndCantAllGoBack_SaysWhereThisPcsClassesAre()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        var homes = lab.Snapshot().Homes;
        var turmas = Path.Combine(lab.NewRoot, "Turmas");
        var staging = Path.Combine(lab.NewRoot, "Turmas (recebendo)");
        var aside = Path.Combine(lab.NewRoot, OwnAside);
        lab.NewStore.MoveHook = (from, to) =>
        {
            if ((Same(from, staging) || Same(from, aside)) && Same(to, turmas))
            {
                throw new IOException("em uso");
            }
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.False(result.Ok);
        Assert.Equal(
            ClassFolderText.TakeOverNotPutBack(
                OldHome, ClassFolderText.CopyNotSaved(ErrorText.Describe(new IOException("em uso"))), copyStayed: false, ownAside: OwnAside),
            result.Detail);
        Assert.DoesNotContain("nada mudou", result.Detail);
        Assert.True(File.Exists(Path.Combine(aside, "Antiga", "velho.txt")));
        Assert.False(Directory.Exists(turmas));
        Assert.Equal(homes, lab.Snapshot().Homes);
        Assert.Null(ClassHomeFileMaster.Read(lab.NewHomePath));
        lab.AssertNoStaging();
    }

    // The room's PCs refuse the claim; the copy's files go back, but what came
    // with them can't: the text doesn't say the copy is in Turmas (it isn't),
    // and says where this PC's own classes are.
    [Fact]
    public async Task AnUndoThatStopsAfterTheFilesWentBack_DoesntSayTheCopyIsInTurmas()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        var index = Path.Combine(lab.NewRoot, "dados", "turmas");
        lab.Taker.SendClaim = (_, _) => (false, ClassFolderText.EndLessonFirst, false);
        lab.NewStore.MoveHook = (from, to) =>
        {
            if (Same(from, index) && Same(to, index + " (recebendo)"))
            {
                throw new IOException("em uso");
            }
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.False(result.Ok);
        Assert.Equal(
            ClassFolderText.TakeOverNotPutBack(OldHome, ClassFolderText.EndLessonFirst, copyStayed: false, ownAside: OwnAside),
            result.Detail);
        Assert.False(Directory.Exists(Path.Combine(lab.NewRoot, "Turmas")));
        Assert.True(File.Exists(Path.Combine(lab.NewRoot, OwnAside, "Antiga", "velho.txt")));
        Assert.Equal(OldHome, rig.Pcs[0].Agent.CurrentHome!.Name);
    }

    // What the text says in each case: the copy left in Turmas or not, this PC's own classes aside or not.
    [Theory]
    [InlineData(true, OwnAside,
        "As pastas não passaram do PC PROF-1 para este computador, e nem tudo voltou ao lugar aqui: a pasta Turmas deste "
        + "computador ficou com as turmas copiadas dele, e as turmas que já estavam neste computador estão na pasta "
        + "\"Turmas (antes de 20-10-2026 1330)\". Encerre a aula antes de passar as pastas.")]
    [InlineData(false, OwnAside,
        "As pastas não passaram do PC PROF-1 para este computador, e nem tudo voltou ao lugar aqui: as turmas que já "
        + "estavam neste computador estão na pasta \"Turmas (antes de 20-10-2026 1330)\". Encerre a aula antes de passar as pastas.")]
    [InlineData(true, null,
        "As pastas não passaram do PC PROF-1 para este computador, e nem tudo voltou ao lugar aqui: a pasta Turmas deste "
        + "computador ficou com as turmas copiadas dele. Encerre a aula antes de passar as pastas.")]
    [InlineData(false, null,
        "As pastas não passaram do PC PROF-1 para este computador, e nem tudo voltou ao lugar aqui: confira a pasta Turmas "
        + "deste computador. Encerre a aula antes de passar as pastas.")]
    public void WhatCouldntGoBack_IsSaidAsItIs(bool copyStayed, string? ownAside, string expected)
    {
        Assert.Equal(expected, ClassFolderText.TakeOverNotPutBack(OldHome, ClassFolderText.EndLessonFirst, copyStayed, ownAside));
    }

    private static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
