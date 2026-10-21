using Midview.Master;
using Midview.Protocol;
using Xunit;

public partial class ClassStoreTests
{
    // --- Paths ---

    [Theory]
    [InlineData("../fora.txt")]
    [InlineData("a/../../fora.txt")]
    [InlineData("a/..")]
    [InlineData("/fora.txt")]
    [InlineData("C:/fora.txt")]
    [InlineData("C:\\fora.txt")]
    [InlineData("a\\b.txt")]
    [InlineData("a//b.txt")]
    [InlineData("a/")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("a/./b.txt")]
    [InlineData("CON")]
    [InlineData("a/nul.txt")]
    [InlineData("arquivo.txt:sombra")]
    [InlineData("termina em ponto.")]
    public void PathOf_RejectsEscape(string relativePath)
    {
        MakeClass();

        Assert.Throws<ArgumentException>(() => _store.PathOf(Turma, relativePath));
    }

    [Fact]
    public void PathOf_GivesThePathInsideTheClassFolder()
    {
        MakeClass();

        Assert.Equal(Path.Combine(FolderOf(Turma), "fotos", "mapa.png"), _store.PathOf(Turma, "fotos/mapa.png"));
        Assert.Equal(Path.Combine(FolderOf(Turma), "a.txt"), _store.PathOf(Turma, "a.txt"));
    }

    [Fact]
    public void PathOf_ThroughALink_Throws()
    {
        MakeClass();
        MakeJunction(Path.Combine(FolderOf(Turma), "atalho"), Outside);

        Assert.Throws<ArgumentException>(() => _store.PathOf(Turma, "atalho/x.txt"));
        Assert.Throws<ArgumentException>(() => _store.PathOf(Turma, "atalho"));
    }

    // --- Receiving a file ---

    [Fact]
    public void BeginIncoming_MakesATempFileInTheClassFolder_ThatIsNotListed()
    {
        MakeClass();

        using var stream = _store.BeginIncoming(Turma);
        stream.Write(new byte[] { 1, 2, 3 });
        stream.Flush();

        Assert.Equal(FolderOf(Turma), Path.GetDirectoryName(stream.Name));
        Assert.True(File.Exists(stream.Name));
        Assert.Empty(_store.Files(Turma));
    }

    [Fact]
    public void BeginIncoming_ForAClassThatIsNotThere_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => _store.BeginIncoming("4º A"));
    }

    [Fact]
    public void CommitIncoming_New_RecordsArrivedToday()
    {
        MakeClass();
        var utc = new DateTime(2026, 10, 19, 15, 45, 12, 345, DateTimeKind.Utc).AddTicks(6789);
        var (temp, version) = Incoming(Turma, "conteúdo", utc);

        var stored = _store.CommitIncoming(Turma, temp, "fotos/mapa.png", version, replace: false);

        Assert.Equal("fotos/mapa.png", stored);
        var path = Path.Combine(FolderOf(Turma), "fotos", "mapa.png");
        Assert.Equal("conteúdo", File.ReadAllText(path));
        Assert.Equal(utc, File.GetLastWriteTimeUtc(path));
        Assert.False(File.Exists(temp));
        Assert.Equal(version, _store.Files(Turma)["fotos/mapa.png"]);
        Assert.Equal(Today, _store.ArrivedOn(Turma, "fotos/mapa.png"));
        Assert.True(_store.IsKnown(Turma, "fotos/mapa.png"));
        Assert.False(_store.IsDeleted(Turma, "fotos/mapa.png"));
        Assert.Null(_store.ArrivedOn(Turma, "outro.png"));
        Assert.False(_store.IsKnown(Turma, "outro.png"));
    }

    [Fact]
    public void CommitIncoming_RemembersTheArrivalDayAcrossRestartsAndLaterDays()
    {
        MakeClass();
        Commit(Turma, "a.txt");
        _now = _now.AddDays(3);

        var reloaded = NewStore(_bin);

        Assert.Equal(new DateOnly(2026, 10, 20), reloaded.ArrivedOn(Turma, "A.TXT"));
    }

    [Fact]
    public void CommitIncoming_SameNameNotReplace_GetsAFreeName()
    {
        MakeClass();
        Commit(Turma, "images.jpg", "primeira");

        var second = Commit(Turma, "images.jpg", "segunda");
        var third = Commit(Turma, "Images.JPG", "terceira");

        Assert.Equal("images (2).jpg", second);
        Assert.Equal("Images (3).JPG", third);
        Assert.Equal("primeira", File.ReadAllText(Path.Combine(FolderOf(Turma), "images.jpg")));
        Assert.Equal("segunda", File.ReadAllText(Path.Combine(FolderOf(Turma), "images (2).jpg")));
        Assert.Equal(Today, _store.ArrivedOn(Turma, "images (2).jpg"));
        Assert.Equal(3, _store.Files(Turma).Count);
        Assert.Empty(_bin.Recycled);
    }

    [Fact]
    public void CommitIncoming_SameNameInASubfolder_GetsAFreeNameThere()
    {
        MakeClass();
        Commit(Turma, "fotos/images.jpg", "primeira");

        var second = Commit(Turma, "fotos/images.jpg", "segunda");

        Assert.Equal("fotos/images (2).jpg", second);
        Assert.Equal("segunda", File.ReadAllText(Path.Combine(FolderOf(Turma), "fotos", "images (2).jpg")));
    }

    [Fact]
    public void CommitIncoming_Replace_RecyclesTheOldCopy()
    {
        MakeClass();
        Commit(Turma, "trabalho.docx", "antigo");
        _now = _now.AddDays(1);
        var newer = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

        var stored = Commit(Turma, "trabalho.docx", "novo e maior", replace: true, utc: newer);

        Assert.Equal("trabalho.docx", stored);
        var path = Path.Combine(FolderOf(Turma), "trabalho.docx");
        Assert.Equal("novo e maior", File.ReadAllText(path));
        Assert.Equal(newer, File.GetLastWriteTimeUtc(path));
        // The old copy went to the Recycle Bin from where it was set aside: in the class folder, under a
        // short name that is never listed, with the file's extension.
        var recycled = Assert.Single(_bin.Recycled);
        Assert.Equal(FolderOf(Turma), Path.GetDirectoryName(recycled));
        Assert.StartsWith(".midview-", Path.GetFileName(recycled));
        Assert.True(ClassFolderRules.IsAsideName(Path.GetFileName(recycled)), recycled);
        Assert.EndsWith(".docx", recycled);
        Assert.Equal("antigo", File.ReadAllText(_bin.InBin(recycled)));
        Assert.Equal(new[] { path }, Directory.GetFiles(FolderOf(Turma)));
        // The file is the same file, edited: it didn't arrive again.
        Assert.Equal(new DateOnly(2026, 10, 20), _store.ArrivedOn(Turma, "trabalho.docx"));
        Assert.Single(_store.Files(Turma));
    }

    [Fact]
    public void CommitIncoming_Replace_KeepsTheOldArrivalDay()
    {
        MakeClass();
        Commit(Turma, "trabalho.docx", "antigo");
        var firstDay = Today;
        _now = _now.AddDays(7);

        Commit(Turma, "trabalho.docx", "novo", replace: true);

        Assert.Equal(firstDay, _store.ArrivedOn(Turma, "trabalho.docx"));
    }

    [Fact]
    public void CommitIncoming_Replace_OfAFileThatIsGone_JustPlacesIt()
    {
        MakeClass();

        var stored = Commit(Turma, "novo.txt", "x", replace: true);

        Assert.Equal("novo.txt", stored);
        Assert.Empty(_bin.Recycled);
        Assert.Equal(Today, _store.ArrivedOn(Turma, "novo.txt"));
    }

    [Fact]
    public void CommitIncoming_Replace_WhenTheOldCopyIsInUse_ChangesNothing()
    {
        MakeClass();
        var old = Write(Turma, "trabalho.docx", "antigo");
        var (temp, version) = Incoming(Turma, "novo");
        // Open in a program that doesn't let it be moved (Word, say).
        using var open = new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read);

        var failure = Assert.Throws<IOException>(
            () => _store.CommitIncoming(Turma, temp, "trabalho.docx", version, replace: true));

        Assert.Contains("em uso", ErrorText.Describe(failure));
        open.Dispose();
        Assert.Equal("antigo", File.ReadAllText(old));
        Assert.True(File.Exists(temp));
        Assert.Equal(new[] { old, temp }.Order(), Directory.GetFiles(FolderOf(Turma)).Order()); // nothing set aside
        Assert.Empty(_bin.Recycled);
        Assert.Null(_store.ArrivedOn(Turma, "trabalho.docx"));
    }

    [Fact]
    public void CommitIncoming_BringsBackAFileTheTeacherHadDeleted()
    {
        MakeClass();
        var path = Write(Turma, "a.txt");
        _store.RefreshTombstones(Turma);
        File.Delete(path);
        _store.RefreshTombstones(Turma);
        Assert.True(_store.IsDeleted(Turma, "a.txt"));

        Commit(Turma, "a.txt", "de volta");

        Assert.False(_store.IsDeleted(Turma, "a.txt"));
        Assert.Equal(Today, _store.ArrivedOn(Turma, "a.txt"));
    }

    [Fact]
    public void CommitIncoming_RefusesAnUnsafePath_AndLeavesTheTempFile()
    {
        MakeClass();
        var (temp, version) = Incoming(Turma, "x");

        Assert.Throws<ArgumentException>(() => _store.CommitIncoming(Turma, temp, "../fora.txt", version, replace: false));
        Assert.Throws<ArgumentException>(() => _store.CommitIncoming(Turma, temp, "C:/fora.txt", version, replace: true));

        Assert.True(File.Exists(temp));
        Assert.Empty(_store.Files(Turma));
    }

    // A file stored in a folder the teacher hid would never be listed, and so pulled again every round
    // ("(2)", "(3)"...): it is refused, saying why, and the temp file is the caller's to delete.
    [Theory]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    public void CommitIncoming_RefusesAFolderThatIsHiddenOrSystem(FileAttributes attribute)
    {
        MakeClass();
        var hidden = Directory.CreateDirectory(Path.Combine(FolderOf(Turma), "fotos", "privadas"));
        hidden.Attributes |= attribute;
        var (temp, version) = Incoming(Turma, "x");

        var refusal = Assert.Throws<IOException>(
            () => _store.CommitIncoming(Turma, temp, "fotos/privadas/mais/a.txt", version, replace: false));

        Assert.Equal("O arquivo não pode ficar na turma: a pasta fotos/privadas está oculta.", refusal.Message);
        Assert.True(File.Exists(temp));
        Assert.False(Directory.Exists(Path.Combine(hidden.FullName, "mais")));
        Assert.Null(_store.ArrivedOn(Turma, "fotos/privadas/mais/a.txt"));
        Assert.Equal("fotos/a.txt", _store.CommitIncoming(Turma, temp, "fotos/a.txt", version, replace: false));
    }

    [Fact]
    public void CommitIncoming_RefusesATempFileOutsideTheClassFolder()
    {
        MakeClass();
        Directory.CreateDirectory(Outside);
        var elsewhere = Path.Combine(Outside, "x.bin");
        File.WriteAllText(elsewhere, "x");

        Assert.Throws<ArgumentException>(
            () => _store.CommitIncoming(Turma, elsewhere, "a.txt", VersionOf("x"), replace: false));
        Assert.True(File.Exists(elsewhere));
    }

    [Fact]
    public void CommitIncoming_ThroughALink_WritesNothingOutside()
    {
        MakeClass();
        MakeJunction(Path.Combine(FolderOf(Turma), "atalho"), Outside);
        var (temp, version) = Incoming(Turma, "x");

        Assert.Throws<ArgumentException>(() => _store.CommitIncoming(Turma, temp, "atalho/x.txt", version, replace: false));

        Assert.Empty(Directory.GetFileSystemEntries(Outside));
    }

    [Fact]
    public void CommitIncoming_RefusesAFileThatIsNotTheSizeAnnounced()
    {
        MakeClass();
        var (temp, _) = Incoming(Turma, "abc");

        var failure = Assert.Throws<IOException>(
            () => _store.CommitIncoming(Turma, temp, "a.txt", new FileVersion(99, VersionOf("abc").LastWriteTicks), replace: false));

        Assert.Contains("tamanho", failure.Message);
        Assert.Empty(_store.Files(Turma));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    public void CommitIncoming_RefusesATimeWindowsCantStore(long ticks)
    {
        MakeClass();
        var (temp, _) = Incoming(Turma, "abc");

        Assert.Throws<IOException>(
            () => _store.CommitIncoming(Turma, temp, "a.txt", new FileVersion(3, ticks), replace: false));

        Assert.Empty(_store.Files(Turma));
    }

    [Fact]
    public void CommitIncoming_ForAClassThatIsNotThere_Throws()
    {
        MakeClass();
        var (temp, version) = Incoming(Turma, "x");
        Directory.Delete(FolderOf(Turma), recursive: true);

        Assert.Throws<DirectoryNotFoundException>(() => _store.CommitIncoming(Turma, temp, "a.txt", version, replace: false));
    }

    // --- The per-PC records ---

    [Fact]
    public void RecordSynced_IsRememberedPerMachine_AndAcrossRestarts()
    {
        MakeClass();
        var one = new FileVersion(10, 111);
        var two = new FileVersion(20, 222);

        _store.RecordSynced(Turma, "AAAA", "fotos/mapa.png", one);
        _store.RecordSynced(Turma, "BBBB", "fotos/mapa.png", two);
        _store.RecordSynced(Turma, "AAAA", "outro.txt", two);

        Assert.Equal(one, _store.LastSynced(Turma, "AAAA", "fotos/mapa.png"));
        Assert.Equal(one, _store.LastSynced(Turma, "aaaa", "FOTOS/MAPA.PNG"));
        Assert.Equal(two, _store.LastSynced(Turma, "BBBB", "fotos/mapa.png"));
        Assert.Null(_store.LastSynced(Turma, "CCCC", "fotos/mapa.png"));
        Assert.Null(_store.LastSynced(Turma, "AAAA", "nunca.txt"));

        _store.SaveIndex(Turma);
        var reloaded = NewStore(_bin);

        Assert.Equal(one, reloaded.LastSynced(Turma, "AAAA", "fotos/mapa.png"));
        Assert.Equal(two, reloaded.LastSynced(Turma, "AAAA", "outro.txt"));
        Assert.Equal(two, reloaded.LastSynced(Turma, "BBBB", "fotos/mapa.png"));
    }

    [Fact]
    public void RecordSynced_IsOnlyInMemoryUntilSaveIndex()
    {
        MakeClass();

        for (var file = 0; file < 150; file++)
        {
            _store.RecordSynced(Turma, $"PC{file % 3}", $"fotos/imagem-{file}.jpg", new FileVersion(file, file + 1000));
        }

        // Nothing is written yet: not even an index file, and a store started now has none of it.
        Assert.False(File.Exists(IndexFileOf(Turma)));
        Assert.Null(NewStore(_bin).LastSynced(Turma, "PC0", "fotos/imagem-0.jpg"));
        Assert.Equal(new FileVersion(3, 1003), _store.LastSynced(Turma, "PC0", "fotos/imagem-3.jpg"));

        _store.SaveIndex(Turma);

        Assert.True(File.Exists(IndexFileOf(Turma)));
        Assert.False(File.Exists(IndexFileOf(Turma) + ".tmp"));
        var reloaded = NewStore(_bin);
        for (var file = 0; file < 150; file++)
        {
            Assert.Equal(new FileVersion(file, file + 1000), reloaded.LastSynced(Turma, $"PC{file % 3}", $"fotos/imagem-{file}.jpg"));
        }
    }

    [Fact]
    public void SaveIndex_WritesOnlyWhatChanged()
    {
        MakeClass();
        _store.RecordSynced(Turma, "AAAA", "a.txt", new FileVersion(1, 1));
        _store.SaveIndex(Turma);
        var longAgo = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(IndexFileOf(Turma), longAgo);

        // Nothing new, and a record that is what it already was: no write.
        _store.SaveIndex(Turma);
        _store.RecordSynced(Turma, "AAAA", "A.TXT", new FileVersion(1, 1));
        _store.SaveIndex(Turma);
        Assert.Equal(longAgo, File.GetLastWriteTimeUtc(IndexFileOf(Turma)));

        _store.RecordSynced(Turma, "AAAA", "a.txt", new FileVersion(2, 1));
        Assert.Equal(longAgo, File.GetLastWriteTimeUtc(IndexFileOf(Turma)));
        _store.SaveIndex(Turma);
        Assert.True(File.GetLastWriteTimeUtc(IndexFileOf(Turma)) > longAgo);
        Assert.Equal(new FileVersion(2, 1), NewStore(_bin).LastSynced(Turma, "AAAA", "a.txt"));

        // Saved, so no longer pending.
        File.SetLastWriteTimeUtc(IndexFileOf(Turma), longAgo);
        _store.SaveIndex(Turma);
        Assert.Equal(longAgo, File.GetLastWriteTimeUtc(IndexFileOf(Turma)));
    }

    [Fact]
    public void AnyOtherChange_SavesAtOnce_AndCarriesTheRecordsNotYetSavedAlong()
    {
        MakeClass();
        _store.RecordSynced(Turma, "AAAA", "a.txt", new FileVersion(1, 1));
        Assert.False(File.Exists(IndexFileOf(Turma)));

        Commit(Turma, "b.txt");

        var reloaded = NewStore(_bin);
        Assert.Equal(Today, reloaded.ArrivedOn(Turma, "b.txt"));
        Assert.Equal(new FileVersion(1, 1), reloaded.LastSynced(Turma, "AAAA", "a.txt"));
        // Nothing left pending.
        var longAgo = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(IndexFileOf(Turma), longAgo);
        _store.SaveIndex(Turma);
        Assert.Equal(longAgo, File.GetLastWriteTimeUtc(IndexFileOf(Turma)));
    }

    [Fact]
    public void SaveIndex_OfAClassThatHasNothingPending_OrIsNoClassName()
    {
        _store.SaveIndex(Turma); // not even loaded
        Assert.False(File.Exists(IndexFileOf(Turma)));

        Assert.Throws<ArgumentException>(() => _store.SaveIndex(".."));
    }

    [Fact]
    public void ForgetMachine_DropsOnlyThatMachinesRecords_WhenSaved()
    {
        MakeClass();
        _store.RecordSynced(Turma, "AAAA", "a.txt", new FileVersion(1, 1));
        _store.RecordSynced(Turma, "BBBB", "a.txt", new FileVersion(2, 2));
        _store.SaveIndex(Turma);

        _store.ForgetMachine(Turma, "AAAA");
        _store.ForgetMachine(Turma, "ZZZZ");

        Assert.Null(_store.LastSynced(Turma, "AAAA", "a.txt"));
        Assert.Equal(new FileVersion(2, 2), _store.LastSynced(Turma, "BBBB", "a.txt"));
        // Like a record, it waits for SaveIndex.
        Assert.Equal(new FileVersion(1, 1), NewStore(_bin).LastSynced(Turma, "AAAA", "a.txt"));

        _store.SaveIndex(Turma);

        Assert.Null(NewStore(_bin).LastSynced(Turma, "AAAA", "a.txt"));
        Assert.Equal(new FileVersion(2, 2), NewStore(_bin).LastSynced(Turma, "BBBB", "a.txt"));
    }

    [Fact]
    public void RecordSynced_FromManyThreads_KeepsEveryRecord()
    {
        MakeClass();

        Parallel.For(0, 120, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            _store.RecordSynced(Turma, $"PC{i % 4}", $"arquivo-{i}.txt", new FileVersion(i, i));
            if (i % 10 == 0)
            {
                _store.SaveIndex(Turma); // the rounds of other PCs save while this one records
            }
        });
        _store.SaveIndex(Turma);

        var reloaded = NewStore(_bin);
        for (var i = 0; i < 120; i++)
        {
            Assert.Equal(new FileVersion(i, i), _store.LastSynced(Turma, $"PC{i % 4}", $"arquivo-{i}.txt"));
            Assert.Equal(new FileVersion(i, i), reloaded.LastSynced(Turma, $"PC{i % 4}", $"arquivo-{i}.txt"));
        }
    }
}
