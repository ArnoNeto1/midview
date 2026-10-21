using System.Security.AccessControl;
using System.Security.Principal;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassStore when something on the disk goes wrong or isn't as the sync expects.
public partial class ClassStoreTests
{
    // --- A path no listing would show ---

    [Theory]
    [InlineData(114)]
    [InlineData(116)]
    [InlineData(118)]
    [InlineData(120)]
    public void CommitIncoming_ACollisionOfALongName_GetsAShorterStem_SoItIsStillListed(int length)
    {
        MakeClass();
        var name = new string('n', length - 4) + ".jpg";

        var first = Commit(Turma, name, "um");
        var second = Commit(Turma, name, "dois");
        var third = Commit(Turma, name, "tres");

        Assert.Equal(name, first);
        Assert.EndsWith(" (2).jpg", second);
        Assert.EndsWith(" (3).jpg", third);
        var all = new[] { first, second, third };
        Assert.All(all, path => Assert.True(path.Length <= FileNames.MaxNameLength, path));
        // Listed, and so not copied again next round, and not dropped from the index either.
        Assert.Equal(all.Order(StringComparer.Ordinal), _store.Files(Turma).Keys.Order(StringComparer.Ordinal));
        _store.RefreshTombstones(Turma);
        Assert.All(all, path =>
        {
            Assert.Equal(Today, _store.ArrivedOn(Turma, path));
            Assert.False(_store.IsDeleted(Turma, path));
        });
        Assert.Equal("dois", File.ReadAllText(Path.Combine(FolderOf(Turma), second)));
        Assert.Equal(3, Directory.GetFiles(FolderOf(Turma)).Length);
    }

    [Fact]
    public void CommitIncoming_ACollisionNearTheWireLimit_GetsAShorterStemToo()
    {
        MakeClass();
        // Four folders of 100 two-byte letters, then a name of 107: 1022 bytes of 1024, and 1026 with " (2)".
        var name = string.Join('/', Enumerable.Repeat(new string('ã', 100), 4)) + "/" + new string('ã', 107) + ".jpg";
        Assert.True(ClassFolderRules.FitsTheWire(name));

        var first = Commit(Turma, name, "um");
        var second = Commit(Turma, name, "dois");

        Assert.Equal(name, first);
        Assert.EndsWith(" (2).jpg", second);
        Assert.True(ClassFolderRules.FitsTheWire(second));
        Assert.Equal(new[] { first, second }.Order(StringComparer.Ordinal), _store.Files(Turma).Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("a/b/c/d/e/f/g/h/nove.txt", "pastas demais", false)]
    [InlineData("a/b/c/d/e/f/g/h/nove.txt", "pastas demais", true)]
    [InlineData("fotos/baixando.crdownload", "nome do arquivo", false)]
    [InlineData("fotos/baixando.crdownload", "nome do arquivo", true)]
    [InlineData("~$trabalho.docx", "nome do arquivo", false)]
    [InlineData(".midview-qualquer.dat", "nome do arquivo", true)]
    public void CommitIncoming_RefusesAPathNoListingWouldShow_AndTouchesNothing(string path, string reason, bool replace)
    {
        MakeClass();
        var (temp, version) = Incoming(Turma, "x");

        var failure = Assert.Throws<IOException>(() => _store.CommitIncoming(Turma, temp, path, version, replace));

        Assert.Contains("não pode ficar na turma", failure.Message);
        Assert.Contains(reason, failure.Message);
        Assert.True(File.Exists(temp));
        Assert.Empty(Directory.GetDirectories(FolderOf(Turma)));
        Assert.Equal(new[] { temp }, Directory.GetFiles(FolderOf(Turma)));
        Assert.Null(_store.ArrivedOn(Turma, path));
    }

    [Fact]
    public void CommitIncoming_RefusesAPathTooLongForTheWire_AndAFileTooBigToSend()
    {
        MakeClass();
        var (temp, version) = Incoming(Turma, "x");
        var folders = string.Concat(Enumerable.Repeat(new string('ã', 100) + "/", 5));

        var wire = Assert.Throws<IOException>(
            () => _store.CommitIncoming(Turma, temp, folders + new string('ã', 12) + ".txt", version, replace: false));
        var size = Assert.Throws<IOException>(
            () => _store.CommitIncoming(Turma, temp, "video.mp4", version with { Size = FileTransfer.MaxFileSize + 1 }, replace: false));

        Assert.Contains("longo demais", wire.Message);
        Assert.Contains("grande demais", size.Message);
        Assert.Empty(_store.Files(Turma));
    }

    // --- A replaced copy ---

    [Fact]
    public void CommitIncoming_Replace_WhenTheRecycleBinRefuses_KeepsTheNewCopy_AndLosesNothing()
    {
        MakeClass();
        var old = Write(Turma, "trabalho.docx", "antigo");
        _bin.RefuseEverything = true;
        var (temp, version) = Incoming(Turma, "novo");

        var stored = _store.CommitIncoming(Turma, temp, "trabalho.docx", version, replace: true);

        Assert.Equal("trabalho.docx", stored);
        Assert.Equal("novo", File.ReadAllText(old));
        Assert.Equal((DateOnly?)Today, _store.ArrivedOn(Turma, "trabalho.docx"));
        // The old copy is still in the class folder, under a name that is never listed.
        var aside = Assert.Single(Directory.GetFiles(FolderOf(Turma)), file => file != old);
        Assert.StartsWith(".midview-", Path.GetFileName(aside));
        Assert.EndsWith("-trabalho.docx", aside);
        Assert.Equal("antigo", File.ReadAllText(aside));
        Assert.Equal(new[] { "trabalho.docx" }, _store.Files(Turma).Keys);
        Assert.Contains(_log, message => message.Contains(aside));
        Assert.Empty(_bin.Recycled);

        // Once the Recycle Bin takes it, it goes: at the end of the lesson, or with the next replaced file.
        _bin.RefuseEverything = false;
        _store.EndLesson(Turma, EndChoice.Keep);

        Assert.False(File.Exists(aside));
        Assert.Equal(new[] { aside }, _bin.Recycled);
        Assert.Equal("antigo", File.ReadAllText(_bin.InBin(aside)));
        Assert.Equal("novo", File.ReadAllText(old));
    }

    [Fact]
    public void CommitIncoming_Replace_WhenTheRecycleBinRefuses_TheNextReplaceTriesAgain()
    {
        MakeClass();
        Write(Turma, "um.docx", "antigo");
        Write(Turma, "dois.docx", "antigo");
        _bin.RefuseEverything = true;
        Commit(Turma, "um.docx", "novo", replace: true);
        _bin.RefuseEverything = false;

        Commit(Turma, "dois.docx", "novo", replace: true);

        Assert.Equal(2, _bin.Recycled.Count);
        Assert.Equal(new[] { "dois.docx", "um.docx" }, Directory.GetFiles(FolderOf(Turma)).Select(file => Path.GetFileName(file)).Order());
    }

    [Fact]
    public void CommitIncoming_Replace_WhenTheFinalMoveFails_PutsTheOldCopyBack()
    {
        MakeClass();
        var yesterday = NowUtc.AddDays(-1);
        var old = Write(Turma, "trabalho.docx", "antigo", yesterday);
        var (temp, version) = Incoming(Turma, "novo");
        // The new file can't be moved while something has it open without sharing deletes (the old copy goes aside first).
        using var holding = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        Assert.Throws<IOException>(() => _store.CommitIncoming(Turma, temp, "trabalho.docx", version, replace: true));

        Assert.Equal("antigo", File.ReadAllText(old));
        Assert.Equal(yesterday, File.GetLastWriteTimeUtc(old));
        Assert.Equal(new[] { old, temp }.Order(), Directory.GetFiles(FolderOf(Turma)).Order()); // no copy left aside
        Assert.Empty(_bin.Recycled);
        Assert.Null(_store.ArrivedOn(Turma, "trabalho.docx"));
        holding.Dispose();

        // And once it can be moved, the same commit goes through.
        Assert.Equal("trabalho.docx", _store.CommitIncoming(Turma, temp, "trabalho.docx", version, replace: true));
        Assert.Equal("novo", File.ReadAllText(old));
    }

    // --- An index that can't be saved ---

    private string BlockIndexSaves(string turma)
    {
        // The index is written to "<index>.tmp" and swapped in: a folder there makes that fail.
        var blocker = IndexFileOf(turma) + ".tmp";
        Directory.CreateDirectory(blocker);
        return blocker;
    }

    [Fact]
    public void CommitIncoming_WhenTheIndexCantBeSaved_StillCommits_AndTheIndexIsSavedLater()
    {
        MakeClass();
        var blocker = BlockIndexSaves(Turma);
        var (temp, version) = Incoming(Turma, "x");

        var stored = _store.CommitIncoming(Turma, temp, "a.txt", version, replace: false);

        Assert.Equal("a.txt", stored);
        Assert.True(File.Exists(Path.Combine(FolderOf(Turma), "a.txt")));
        Assert.False(File.Exists(temp));
        Assert.Equal(Today, _store.ArrivedOn(Turma, "a.txt"));
        Assert.Contains(_log, message => message.Contains("could not be saved"));
        Assert.False(File.Exists(IndexFileOf(Turma)));

        // Still pending: SaveIndex tries again, and says if it can't.
        Assert.ThrowsAny<SystemException>(() => _store.SaveIndex(Turma));
        Directory.Delete(blocker);
        _store.SaveIndex(Turma);

        Assert.Equal(Today, NewStore(_bin).ArrivedOn(Turma, "a.txt"));
    }

    [Fact]
    public void EndLesson_WhenTheIndexCantBeSaved_StillReturnsWhatItDid_AndTheIndexIsSavedLater()
    {
        MakeClass();
        Commit(Turma, "a.txt");
        _store.RecordSynced(Turma, "AAAA", "a.txt", new FileVersion(1, 1));
        var blocker = BlockIndexSaves(Turma);

        var (recycled, problems) = _store.EndLesson(Turma, EndChoice.DeleteAll);

        Assert.Equal(1, recycled);
        Assert.Empty(problems);
        Assert.Empty(Directory.GetFiles(FolderOf(Turma)));
        Assert.Contains(_log, message => message.Contains("could not be saved"));

        Directory.Delete(blocker);
        _store.SaveIndex(Turma);
        var reloaded = NewStore(_bin);
        Assert.False(reloaded.IsKnown(Turma, "a.txt"));
        Assert.Null(reloaded.LastSynced(Turma, "AAAA", "a.txt"));
    }

    [Fact]
    public void AnyOtherSaveThatFails_LeavesTheIndexPending_ForSaveIndexToRetry()
    {
        MakeClass();
        Write(Turma, "a.txt");
        var blocker = BlockIndexSaves(Turma);

        Assert.ThrowsAny<SystemException>(() => _store.RefreshTombstones(Turma));
        Assert.Equal(LocalDay(DefaultWriteUtc), _store.ArrivedOn(Turma, "a.txt"));
        Directory.Delete(blocker);
        _store.SaveIndex(Turma);

        Assert.Equal(LocalDay(DefaultWriteUtc), NewStore(_bin).ArrivedOn(Turma, "a.txt"));
    }

    // --- A folder that can't be read ---

    [Fact]
    public void Files_ASubfolderThatCantBeRead_Throws_InsteadOfListingLess()
    {
        MakeClass();
        Write(Turma, "a.txt");
        Write(Turma, "sub/b.txt");
        _store.RefreshTombstones(Turma);
        var sub = Path.Combine(FolderOf(Turma), "sub");
        CloseToTheTeacher(sub);

        var failure = Assert.Throws<IOException>(() => _store.Files(Turma));

        Assert.Contains("sub", failure.Message);
        Assert.Contains("acesso negado", failure.Message);
        // No tombstone comes out of a listing that left a folder out.
        Assert.Throws<IOException>(() => _store.RefreshTombstones(Turma));
        Assert.False(_store.IsDeleted(Turma, "sub/b.txt"));
        Assert.True(_store.IsKnown(Turma, "sub/b.txt"));
        // A summary for the window makes do with what it can see.
        Assert.Equal(1, _store.List().Single().Files);

        OpenToTheTeacher(sub);

        Assert.Equal(new[] { "a.txt", "sub/b.txt" }, _store.Files(Turma).Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Files_WhatIsSkippedOnPurpose_IsNotAnError()
    {
        MakeClass();
        Write(Turma, "a.txt");
        File.SetAttributes(Write(Turma, "escondido.txt"), FileAttributes.Hidden);
        Write(Turma, "pasta oculta/c.txt");
        var hidden = Path.Combine(FolderOf(Turma), "pasta oculta");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        CloseToTheTeacher(hidden); // never looked into: hidden

        Assert.Equal(new[] { "a.txt" }, _store.Files(Turma).Keys);
    }

    [Fact]
    public void EndLesson_WhenAFolderCantBeRead_StillEnds_AndSaysSo()
    {
        MakeClass();
        Commit(Turma, "a.txt");
        Commit(Turma, "sub/b.txt");
        var sub = Path.Combine(FolderOf(Turma), "sub");
        CloseToTheTeacher(sub);

        var (recycled, problems) = _store.EndLesson(Turma, EndChoice.DeleteToday);

        Assert.Equal(2, recycled);
        var problem = Assert.Single(problems);
        Assert.Contains("sub", problem);
        Assert.Contains("acesso negado", problem);
    }

    // --- Links, and what is not one ---

    [Fact]
    public void Files_ListsAFileWhoseReparsePointIsNoLink()
    {
        MakeClass();
        Write(Turma, "meu.txt");
        NonLinkReparsePoint.Make(Write(Turma, "nuvem.txt", "conteudo"));

        var files = _store.Files(Turma);

        Assert.Equal(new[] { "meu.txt", "nuvem.txt" }, files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(8, files["nuvem.txt"].Size);
        Assert.Equal(Path.Combine(FolderOf(Turma), "nuvem.txt"), _store.PathOf(Turma, "nuvem.txt"));
    }

    [Fact]
    public void EndLesson_DeleteAll_RecyclesAFileWhoseReparsePointIsNoLink_LikeAnyFile()
    {
        MakeClass();
        NonLinkReparsePoint.Make(Write(Turma, "nuvem.txt", "conteudo"));

        var (recycled, problems) = _store.EndLesson(Turma, EndChoice.DeleteAll);

        Assert.Equal(1, recycled);
        Assert.Empty(problems);
        Assert.Empty(Directory.GetFiles(FolderOf(Turma)));
    }

    // A folder the teacher's account can't look into, as an ACL can make it.
    private void CloseToTheTeacher(string folder)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.AddAccessRule(NoListing());
        info.SetAccessControl(security);
        _closed.Add(folder);
    }

    private static void OpenToTheTeacher(string folder)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.RemoveAccessRuleSpecific(NoListing());
        info.SetAccessControl(security);
    }

    private static FileSystemAccessRule NoListing() =>
        new(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
}
