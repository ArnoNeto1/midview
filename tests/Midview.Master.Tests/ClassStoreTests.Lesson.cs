using Midview.Master;
using Midview.Protocol;
using Xunit;

public partial class ClassStoreTests
{
    // --- What the teacher does in Explorer ---

    [Fact]
    public void RefreshTombstones_MarksTeacherDeletions()
    {
        MakeClass();
        Commit(Turma, "a.txt");
        Commit(Turma, "sub/b.txt");
        Commit(Turma, "sub/c.txt");
        File.Delete(Path.Combine(FolderOf(Turma), "a.txt"));
        Directory.Delete(Path.Combine(FolderOf(Turma), "sub"), recursive: true);
        Write(Turma, "sub/c.txt"); // a new "c.txt", put back by the teacher

        _store.RefreshTombstones(Turma);

        Assert.True(_store.IsDeleted(Turma, "a.txt"));
        Assert.True(_store.IsDeleted(Turma, "SUB/B.TXT"));
        Assert.True(_store.IsKnown(Turma, "a.txt"));
        Assert.Null(_store.ArrivedOn(Turma, "a.txt"));
        Assert.False(_store.IsDeleted(Turma, "sub/c.txt"));
        Assert.True(_store.IsKnown(Turma, "sub/c.txt"));
        Assert.True(NewStore(_bin).IsDeleted(Turma, "a.txt"));
        Assert.False(_store.IsDeleted(Turma, "nunca-existiu.txt"));
    }

    [Fact]
    public void RefreshTombstones_IndexesWhatTheTeacherAdded_WithTheDayOfItsOwnTime()
    {
        MakeClass();
        var yesterday = Today.AddDays(-1);
        Write(Turma, "de-ontem.pdf", "x", NowUtc.AddDays(-1));
        Write(Turma, "de-hoje.pdf", "x", NowUtc);
        Write(Turma, "de-semana-passada.pdf", "x", NowUtc.AddDays(-8));
        Assert.False(_store.IsKnown(Turma, "de-ontem.pdf"));

        _store.RefreshTombstones(Turma);
        _now = _now.AddDays(2);
        _store.RefreshTombstones(Turma);

        Assert.Equal(yesterday, _store.ArrivedOn(Turma, "de-ontem.pdf"));
        Assert.Equal(yesterday.AddDays(1), _store.ArrivedOn(Turma, "de-hoje.pdf"));
        Assert.Equal(yesterday.AddDays(-7), _store.ArrivedOn(Turma, "de-semana-passada.pdf"));
        Assert.Equal(yesterday, NewStore(_bin).ArrivedOn(Turma, "de-ontem.pdf"));
    }

    [Fact]
    public void RefreshTombstones_TheDayOfAFile_IsTheTeachersLocalOne_NotTheUtcOne()
    {
        MakeClass();
        // Late in the evening and just after midnight, local time: in any time zone but UTC itself,
        // one of the two is on another day in UTC.
        var late = new DateTime(2026, 10, 19, 23, 30, 0, DateTimeKind.Local);
        var early = new DateTime(2026, 10, 19, 0, 30, 0, DateTimeKind.Local);
        Write(Turma, "de-noite.pdf", "x", late.ToUniversalTime());
        Write(Turma, "de-madrugada.pdf", "x", early.ToUniversalTime());

        _store.RefreshTombstones(Turma);

        Assert.Equal(new DateOnly(2026, 10, 19), _store.ArrivedOn(Turma, "de-noite.pdf"));
        Assert.Equal(new DateOnly(2026, 10, 19), _store.ArrivedOn(Turma, "de-madrugada.pdf"));
    }

    [Fact]
    public void RefreshTombstones_AFileWithATimeInTheFuture_IsNotToday_UntilThen()
    {
        MakeClass();
        Write(Turma, "do-futuro.pdf", "x", NowUtc.AddDays(3));

        _store.RefreshTombstones(Turma);

        Assert.Equal(Today.AddDays(3), _store.ArrivedOn(Turma, "do-futuro.pdf"));
    }

    [Fact]
    public void RefreshTombstones_ThenTheTeacherDeletesWhatHeAdded_ItIsATombstone()
    {
        MakeClass();
        var path = Write(Turma, "do-professor.pdf");
        _store.RefreshTombstones(Turma);
        File.Delete(path);

        _store.RefreshTombstones(Turma);

        Assert.True(_store.IsDeleted(Turma, "do-professor.pdf"));
    }

    [Fact]
    public void RefreshTombstones_AFileThatCameBack_IsNoLongerDeleted()
    {
        MakeClass();
        var path = Write(Turma, "a.txt");
        _store.RefreshTombstones(Turma);
        File.Delete(path);
        _store.RefreshTombstones(Turma);
        Assert.True(_store.IsDeleted(Turma, "a.txt"));

        Write(Turma, "a.txt");
        _store.RefreshTombstones(Turma);

        Assert.False(_store.IsDeleted(Turma, "a.txt"));
        Assert.Equal(LocalDay(DefaultWriteUtc), _store.ArrivedOn(Turma, "a.txt"));
    }

    [Fact]
    public void RefreshTombstones_WhenTheClassFolderIsGone_DeletesNothing()
    {
        MakeClass();
        Commit(Turma, "a.txt");
        Directory.Delete(FolderOf(Turma), recursive: true);

        _store.RefreshTombstones(Turma);

        Assert.False(_store.IsDeleted(Turma, "a.txt"));
        Assert.Equal(Today, _store.ArrivedOn(Turma, "a.txt"));
    }

    [Fact]
    public void RefreshTombstones_AFileThatIsOnlyHiddenNow_IsNotTakenForDeleted()
    {
        MakeClass();
        var path = Write(Turma, "a.txt");
        _store.RefreshTombstones(Turma);
        File.SetAttributes(path, FileAttributes.Hidden);

        _store.RefreshTombstones(Turma);

        Assert.False(_store.IsDeleted(Turma, "a.txt"));
        Assert.Equal(LocalDay(DefaultWriteUtc), _store.ArrivedOn(Turma, "a.txt"));
    }

    // --- Ending a lesson ---

    [Fact]
    public void EndLesson_DeleteToday_KeepsOlderFiles()
    {
        MakeClass();
        Commit(Turma, "ontem.txt", "o");
        Commit(Turma, "fotos/ontem.png", "o");
        _now = _now.AddDays(1);
        Commit(Turma, "hoje.txt", "h");
        Commit(Turma, "fotos/hoje.png", "h");
        Commit(Turma, "pasta/so-hoje.png", "h");
        // Never indexed: each arrives the day of its own time.
        Write(Turma, "do-professor.txt", "p", NowUtc);
        Write(Turma, "do-professor-antigo.txt", "p", NowUtc.AddDays(-3));
        _store.RecordSynced(Turma, "AAAA", "hoje.txt", new FileVersion(1, 1));

        var (recycled, problems) = _store.EndLesson(Turma, EndChoice.DeleteToday);

        Assert.Equal(4, recycled);
        Assert.Empty(problems);
        Assert.Equal(
            new[] { "do-professor-antigo.txt", "fotos/ontem.png", "ontem.txt" },
            _store.Files(Turma).Keys.Order(StringComparer.Ordinal));
        Assert.Equal(4, _bin.Recycled.Count);
        Assert.False(Directory.Exists(Path.Combine(FolderOf(Turma), "pasta"))); // emptied, so tidied away
        Assert.True(Directory.Exists(Path.Combine(FolderOf(Turma), "fotos")));
        Assert.Null(_store.ArrivedOn(Turma, "hoje.txt"));
        Assert.Equal(new DateOnly(2026, 10, 20), _store.ArrivedOn(Turma, "ontem.txt"));
        Assert.Null(_store.LastSynced(Turma, "AAAA", "hoje.txt"));
    }

    [Fact]
    public void EndLesson_DeleteAll_RecyclesEverything()
    {
        MakeClass();
        Commit(Turma, "a.txt");
        Commit(Turma, "sub/b.txt");
        Commit(Turma, "sub/fundo/c.txt");
        File.SetAttributes(Write(Turma, "escondido.txt"), FileAttributes.Hidden);
        Write(Turma, "rascunho.tmp");
        Write(Turma, "a/b/c/d/e/f/g/h/fundo.txt");
        Directory.CreateDirectory(Path.Combine(FolderOf(Turma), "vazia"));
        _store.RecordSynced(Turma, "AAAA", "a.txt", new FileVersion(1, 1));

        var (recycled, problems) = _store.EndLesson(Turma, EndChoice.DeleteAll);

        Assert.Equal(6, recycled); // the hidden, the unlisted and the too-deep ones too
        Assert.Empty(problems);
        Assert.True(Directory.Exists(FolderOf(Turma)));
        Assert.Empty(Directory.GetFileSystemEntries(FolderOf(Turma)));
        Assert.Empty(_store.Files(Turma));
        Assert.False(_store.IsKnown(Turma, "a.txt"));
        Assert.Null(_store.LastSynced(Turma, "AAAA", "a.txt"));
        Assert.Equal(new ClassSummary(Turma, 0, 0, null), _store.List().Single());
    }

    [Fact]
    public void EndLesson_Keep_ClearsPerMachineRecords()
    {
        MakeClass();
        Commit(Turma, "a.txt");
        Commit(Turma, "b.txt");
        _store.RecordSynced(Turma, "AAAA", "a.txt", new FileVersion(1, 1));
        _store.RecordSynced(Turma, "BBBB", "b.txt", new FileVersion(1, 1));
        File.Delete(Path.Combine(FolderOf(Turma), "b.txt"));
        _store.RefreshTombstones(Turma);
        Assert.True(_store.IsDeleted(Turma, "b.txt"));
        Write(Turma, "do-professor.txt");

        var (recycled, problems) = _store.EndLesson(Turma, EndChoice.Keep);

        Assert.Equal(0, recycled);
        Assert.Empty(problems);
        Assert.Empty(_bin.Recycled);
        Assert.Equal(new[] { "a.txt", "do-professor.txt" }, _store.Files(Turma).Keys.Order(StringComparer.Ordinal));
        Assert.Null(_store.LastSynced(Turma, "AAAA", "a.txt"));
        Assert.Null(_store.LastSynced(Turma, "BBBB", "b.txt"));
        Assert.False(_store.IsDeleted(Turma, "b.txt"));
        Assert.False(_store.IsKnown(Turma, "b.txt"));
        Assert.Equal(Today, _store.ArrivedOn(Turma, "a.txt"));
        Assert.Equal(LocalDay(DefaultWriteUtc), _store.ArrivedOn(Turma, "do-professor.txt"));
        var reloaded = NewStore(_bin);
        Assert.Null(reloaded.LastSynced(Turma, "AAAA", "a.txt"));
        Assert.False(reloaded.IsDeleted(Turma, "b.txt"));
    }

    [Theory]
    [InlineData(EndChoice.DeleteToday)]
    [InlineData(EndChoice.DeleteAll)]
    public void EndLesson_NamesWhatCouldNotGo_AndKeepsIt(EndChoice choice)
    {
        var bin = new MovingRecycleBin(Path.Combine(_root, "lixeira2"), refused: "aberto.docx");
        var store = NewStore(bin);
        store.Create(Turma);
        Write(Turma, "aberto.docx", "x", NowUtc);
        Write(Turma, "sub/livre.txt", "x", NowUtc);
        store.RefreshTombstones(Turma);

        var (recycled, problems) = store.EndLesson(Turma, choice);

        Assert.Equal(1, recycled);
        Assert.Equal(new[] { "aberto.docx (o arquivo está em uso por outro programa)" }, problems);
        Assert.Equal(new[] { "aberto.docx" }, store.Files(Turma).Keys);
        Assert.Equal(Today, store.ArrivedOn(Turma, "aberto.docx"));
        Assert.Null(store.ArrivedOn(Turma, "sub/livre.txt"));
    }

    [Fact]
    public void EndLesson_DeleteAll_LeavesALinkAloneAndSaysSo()
    {
        MakeClass();
        Commit(Turma, "a.txt");
        File.WriteAllText(Path.Combine(Outside, "importante.txt"), "x");
        MakeJunction(Path.Combine(FolderOf(Turma), "atalho"), Outside);

        var (recycled, problems) = _store.EndLesson(Turma, EndChoice.DeleteAll);

        Assert.Equal(1, recycled);
        Assert.Equal(new[] { "atalho (é um atalho; não foi apagado)" }, problems);
        Assert.True(File.Exists(Path.Combine(Outside, "importante.txt")));
        Assert.True(Directory.Exists(Path.Combine(FolderOf(Turma), "atalho")));
    }

    [Fact]
    public void EndLesson_ThrowsAwayWhatAnInterruptedTransferLeft()
    {
        MakeClass();
        var (temp, _) = Incoming(Turma, "meio");
        Commit(Turma, "a.txt");

        _store.EndLesson(Turma, EndChoice.Keep);

        Assert.False(File.Exists(temp));
        Assert.Equal(new[] { "a.txt" }, _store.Files(Turma).Keys);
    }

    [Fact]
    public void EndLesson_DeleteToday_NeverLeavesTheClassFolder_WhateverAHandEditedIndexSays()
    {
        MakeClass();
        Commit(Turma, "a.txt");
        var precious = Path.Combine(FilesRoot, "fora.txt");
        File.WriteAllText(precious, "x");
        var neighbour = Path.Combine(FilesRoot, "3º B");
        Directory.CreateDirectory(neighbour);
        File.WriteAllText(Path.Combine(neighbour, "vizinha.txt"), "x");
        File.WriteAllText(
            IndexFileOf(Turma),
            """{"ArrivedOn":{"../fora.txt":"2026-10-20","..\\fora.txt":"2026-10-20","../3º B/vizinha.txt":"2026-10-20","a.txt":"2026-10-20"}}""");

        var (recycled, problems) = NewStore(_bin).EndLesson(Turma, EndChoice.DeleteToday);

        Assert.Equal(1, recycled);
        Assert.Empty(problems);
        Assert.True(File.Exists(precious));
        Assert.True(File.Exists(Path.Combine(neighbour, "vizinha.txt")));
        Assert.Empty(_store.Files(Turma));
    }

    [Fact]
    public void EndLesson_OfAClassWhoseFolderIsGone_ClearsTheIndexAndThrowsNothing()
    {
        MakeClass();
        Commit(Turma, "a.txt");
        _store.RecordSynced(Turma, "AAAA", "a.txt", new FileVersion(1, 1));
        Directory.Delete(FolderOf(Turma), recursive: true);

        var (recycled, problems) = _store.EndLesson(Turma, EndChoice.DeleteAll);

        Assert.Equal(0, recycled);
        Assert.Empty(problems);
        Assert.Null(_store.LastSynced(Turma, "AAAA", "a.txt"));
    }

    // --- The index on disk ---

    [Fact]
    public void CorruptIndex_IsSetAside()
    {
        MakeClass();
        Write(Turma, "a.txt");
        Directory.CreateDirectory(IndexRoot);
        File.WriteAllText(IndexFileOf(Turma), "{\"ArrivedOn\": {\"a.txt\": \"2026-");

        var reloaded = NewStore(_bin);

        Assert.Null(reloaded.ArrivedOn(Turma, "a.txt"));
        Assert.Equal("{\"ArrivedOn\": {\"a.txt\": \"2026-", File.ReadAllText(IndexFileOf(Turma) + ".corrupt"));
        Assert.False(File.Exists(IndexFileOf(Turma)));
        // The files on disk are still the class's files, first indexed as arriving the day of their own time.
        Assert.Equal(new[] { "a.txt" }, reloaded.Files(Turma).Keys);
        reloaded.RefreshTombstones(Turma);
        Assert.Equal(LocalDay(DefaultWriteUtc), reloaded.ArrivedOn(Turma, "a.txt"));
        Assert.True(File.Exists(IndexFileOf(Turma)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ALostOrCorruptIndex_DoesNotMakeEveryFileArriveToday(bool corrupt)
    {
        MakeClass();
        Write(Turma, "de-ontem.png", "o", NowUtc.AddDays(-1));
        Write(Turma, "da-semana-passada.png", "s", NowUtc.AddDays(-7));
        Write(Turma, "de-hoje.png", "h", NowUtc);
        if (corrupt)
        {
            Directory.CreateDirectory(IndexRoot);
            File.WriteAllText(IndexFileOf(Turma), "{ \"ArrivedOn\": ");
        }

        var (recycled, problems) = NewStore(_bin).EndLesson(Turma, EndChoice.DeleteToday);

        Assert.Equal(1, recycled);
        Assert.Empty(problems);
        Assert.Equal(new[] { "da-semana-passada.png", "de-ontem.png" }, _store.Files(Turma).Keys.Order(StringComparer.Ordinal));
        Assert.Equal(corrupt, File.Exists(IndexFileOf(Turma) + ".corrupt"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1, 2]")]
    [InlineData("{\"ArrivedOn\": {\"a.txt\": \"not a date\"}}")]
    [InlineData("{\"Machines\": {\"AAAA\": {\"a.txt\": 5}}}")]
    public void AnIndexThatIsNotAnIndex_IsSetAsideToo(string content)
    {
        MakeClass();
        Directory.CreateDirectory(IndexRoot);
        File.WriteAllText(IndexFileOf(Turma), content);

        Assert.False(NewStore(_bin).IsKnown(Turma, "a.txt"));

        Assert.True(File.Exists(IndexFileOf(Turma) + ".corrupt"));
    }

    [Fact]
    public void AnIndexWithMissingParts_IsStillAnIndex()
    {
        MakeClass();
        Directory.CreateDirectory(IndexRoot);
        File.WriteAllText(IndexFileOf(Turma), "{\"Deleted\": [\"a.txt\", null], \"Machines\": {\"AAAA\": null}}");

        var store = NewStore(_bin);

        Assert.True(store.IsDeleted(Turma, "a.txt"));
        Assert.Null(store.ArrivedOn(Turma, "a.txt"));
        Assert.Null(store.LastSynced(Turma, "AAAA", "a.txt"));
        Assert.False(File.Exists(IndexFileOf(Turma) + ".corrupt"));
    }

    [Fact]
    public void TheIndexIsSavedAtOnceByEveryChangeButARecord_WithNoTempFileLeft()
    {
        MakeClass();

        Commit(Turma, "a.txt");
        Assert.True(File.Exists(IndexFileOf(Turma)));
        Assert.False(File.Exists(IndexFileOf(Turma) + ".tmp"));
        Assert.Equal(Today, NewStore(_bin).ArrivedOn(Turma, "a.txt"));

        _store.RecordSynced(Turma, "AAAA", "a.txt", new FileVersion(1, 1));
        _store.SaveIndex(Turma);
        Assert.Equal(new FileVersion(1, 1), NewStore(_bin).LastSynced(Turma, "AAAA", "a.txt"));
        Assert.False(File.Exists(IndexFileOf(Turma) + ".tmp"));
        Assert.Equal(new[] { "3º A.json" }, Directory.GetFiles(IndexRoot).Select(file => Path.GetFileName(file)));
    }

    // Moves what it's given into a folder of its own (a numbered one each time, so the same name
    // twice is fine), or refuses the names it's told to.
    private sealed class MovingRecycleBin : IRecycleBin
    {
        private readonly string _bin;
        private readonly string? _refused;

        public MovingRecycleBin(string bin, string? refused = null)
        {
            _bin = bin;
            _refused = refused;
            Directory.CreateDirectory(bin);
        }

        // What was recycled, by the full path it had.
        public List<string> Recycled { get; } = new();

        // Where what was recycled from path is now.
        public string InBin(string path) => Path.Combine(_bin, Recycled.IndexOf(path).ToString(), Path.GetFileName(path));

        public void Recycle(string path)
        {
            if (Path.GetFileName(path) == _refused)
            {
                throw new IOException("in use", unchecked((int)0x80070020));
            }

            var holder = Path.Combine(_bin, Recycled.Count.ToString());
            Directory.CreateDirectory(holder);
            var target = Path.Combine(holder, Path.GetFileName(path));
            if (Directory.Exists(path))
            {
                Directory.Move(path, target);
            }
            else
            {
                File.Move(path, target);
            }
            Recycled.Add(path);
        }
    }
}
