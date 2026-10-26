using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver: what doesn't come over, and room for what does.
// Files the store doesn't list stay
// only in the old home's copy, and the result says how many and where; the
// copy must fit on each disk it is received on (the index's and the lessons
// map's too, when theirs isn't the class folders' disk).
public sealed partial class ClassFolderHandOverTests
{
    // A loose file in Turmas, one in a folder that can't be a class,
    // a lock file, one still downloading, one too deep and a hidden one: not
    // copied (the PCs never get them either), counted by the old home, and
    // said to be in its copy.
    [Fact]
    public async Task FilesTheStoreDoesntList_StayInTheOldHomesCopy_AndTheResultSaysHowMany()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        var turmas = Path.Combine(rig.Root, "Turmas");
        ClassSyncRig.WriteFile(Path.Combine(turmas, "solto.txt"), new byte[] { 1 }, Earlier);
        ClassSyncRig.WriteFile(Path.Combine(turmas, "Turma ${x}", "a.txt"), new byte[] { 1 }, Earlier);
        rig.WriteInStore("~$aberto.docx", new byte[] { 1 }, Earlier);
        rig.WriteInStore("baixando.crdownload", new byte[] { 1 }, Earlier);
        rig.WriteInStore("1/2/3/4/5/6/7/8/fundo.txt", new byte[] { 1 }, Earlier);
        rig.WriteInStore("oculto.txt", new byte[] { 1 }, Earlier);
        File.SetAttributes(rig.StorePath("oculto.txt"), FileAttributes.Hidden);

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.True(result.Ok, result.Detail);
        Assert.Equal(
            ClassFolderText.TookOver(3, 3) + " " + ClassFolderText.OldCopyAside(OldHome, "Turmas (passadas para PROF-2)") + " "
            + ClassFolderText.LeftOnTheOldHome(OldHome, 6, "Turmas (passadas para PROF-2)"),
            result.Detail);
        Assert.True(File.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)", "solto.txt")));
        Assert.True(File.Exists(Path.Combine(rig.Root, "Turmas (passadas para PROF-2)", Turma, "oculto.txt")));
        Assert.False(File.Exists(lab.NewPath(Turma, "oculto.txt")));
        Assert.False(File.Exists(Path.Combine(lab.NewRoot, "Turmas", "solto.txt")));
    }

    // A home that says a negative number of files stayed behind is refused like any copy that doesn't add up.
    [Fact]
    public async Task AHomeSayingANegativeNumberLeftBehind_IsRefused()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig, openHost: false);
        await lab.MakeTheOldHomeTheHomeAsync();
        var before = lab.Snapshot();
        using var home = new ScriptedHandOverHome(rig, channel =>
        {
            SendFile(channel, Manifest, ClassFolderHandOver.EncodeManifest(new[] { Turma }, Array.Empty<(string, long, long)>(), leftOut: -1));
            Done(channel);
        });

        var result = await lab.Taker.TakeOverAsync(lab.Home with { Port = home.Port });

        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.CopyMismatch(OldHome)), result.Detail);
        lab.AssertUnchanged(before);
    }

    [Fact]
    public void TheLeftOnTheOldHomeTexts_AreTheOnesAgreed()
    {
        Assert.Equal(
            "1 arquivo ficou só na pasta \"Turmas (passadas para PROF-2)\" do PC PROF-1 (arquivos ocultos, grandes demais, "
            + "com nomes que não servem ou fora de uma turma não passam para outro computador).",
            ClassFolderText.LeftOnTheOldHome("PROF-1", 1, "Turmas (passadas para PROF-2)"));
        Assert.Equal(
            "1.234 arquivos ficaram só na pasta \"Turmas\" do PC PROF-1 (arquivos ocultos, grandes demais, "
            + "com nomes que não servem ou fora de uma turma não passam para outro computador).",
            ClassFolderText.LeftOnTheOldHome("PROF-1", 1234, "Turmas"));
        Assert.Equal(
            "2 arquivos ficaram só no PC PROF-1, numa pasta à parte (arquivos ocultos, grandes demais, "
            + "com nomes que não servem ou fora de uma turma não passam para outro computador).",
            ClassFolderText.LeftOnTheOldHome("PROF-1", 2, null));
    }

    // The classes' disk has room, but the one the index and the
    // lessons map are received on doesn't: refused before anything is written.
    [Fact]
    public async Task ACopyWhoseIndexDoesntFitOnItsOwnDisk_IsRefusedBeforeAnythingIsWritten()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.FillTheOldHome();
        var before = lab.Snapshot();
        var data = Path.Combine(lab.NewRoot, "dados");
        lab.NewStore.DiskOf = path => path.StartsWith(data, StringComparison.OrdinalIgnoreCase) ? @"Y:\" : @"X:\";
        var asked = new List<string>();
        lab.NewStore.FreeBytesOnDisk = disk =>
        {
            asked.Add(disk);
            return ClassFolderHandOver.KeepFreeBytes + 10;
        };

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        // What doesn't fit there (the result names that disk): the indexes and the lessons map, as the old home sent them.
        var oldData = Path.Combine(rig.Root, "dados");
        var dataBytes = Directory.GetFiles(Path.Combine(oldData, "turmas"), "*.json").Sum(file => new FileInfo(file).Length)
            + new FileInfo(Path.Combine(oldData, "class-lessons.json")).Length;
        Assert.False(result.Ok);
        Assert.Equal(
            ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.CopyNoRoom(dataBytes, ClassFolderHandOver.KeepFreeBytes + 10, @"Y:\")),
            result.Detail);
        Assert.Equal(new[] { @"Y:\" }, asked); // the classes' disk goes by FreeBytes (NewFreeSpace: plenty)
        lab.AssertUnchanged(before);
    }

    [Fact]
    public void TheNoRoomTexts_AreTheOnesAgreed()
    {
        Assert.Equal(
            "Não há espaço neste computador: as pastas ocupam 2 GB e o disco tem 1,5 GB livres (é preciso sobrar 1 GB).",
            ClassFolderText.CopyNoRoom(2L << 30, 3L << 29));
        Assert.Equal(
            "Não há espaço neste computador: os dados das turmas ocupam 120 KB e o disco D: tem 1 GB livres (é preciso sobrar 1 GB).",
            ClassFolderText.CopyNoRoom(120 * 1024, 1L << 30, @"D:\"));
    }
}
