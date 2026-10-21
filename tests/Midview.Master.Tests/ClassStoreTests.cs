using System.Diagnostics;
using System.Text;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public partial class ClassStoreTests : IDisposable
{
    private const string Turma = "3º A";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-turmas-{Guid.NewGuid():N}");
    private readonly List<string> _links = new();
    private readonly MovingRecycleBin _bin;
    private readonly ClassStore _store;
    private DateTime _now = new(2026, 10, 20, 13, 30, 0);

    public ClassStoreTests()
    {
        Directory.CreateDirectory(Outside);
        _bin = new MovingRecycleBin(Path.Combine(_root, "lixeira"));
        _store = NewStore(_bin);
    }

    public void Dispose()
    {
        // Links first: deleting one removes the link, never what it points at.
        foreach (var link in _links.Where(Directory.Exists))
        {
            Directory.Delete(link);
        }
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string FilesRoot => Path.Combine(_root, "Turmas");

    private string IndexRoot => Path.Combine(_root, "indice");

    private string Outside => Path.Combine(_root, "fora");

    private DateOnly Today => DateOnly.FromDateTime(_now);

    private ClassStore NewStore(IRecycleBin bin) => new(FilesRoot, IndexRoot, bin, () => _now);

    private string FolderOf(string turma) => Path.Combine(FilesRoot, turma);

    private string IndexFileOf(string turma) => Path.Combine(IndexRoot, turma + ".json");

    private string MakeClass(string name = Turma)
    {
        var (ok, detail, turma) = _store.Create(name);
        Assert.True(ok, detail);
        return turma;
    }

    // A file the teacher (or anything but the sync) put in the class folder.
    private string Write(string turma, string relativePath, string content = "x", DateTime? utc = null)
    {
        var path = Path.Combine(FolderOf(turma), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, utc ?? new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc));
        return path;
    }

    private static FileVersion VersionOf(string content, DateTime? utc = null) => new(
        Encoding.UTF8.GetByteCount(content), (utc ?? new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc)).Ticks);

    private (string TempPath, FileVersion Version) Incoming(string turma, string content, DateTime? utc = null)
    {
        string temp;
        using (var stream = _store.BeginIncoming(turma))
        {
            temp = stream.Name;
            stream.Write(Encoding.UTF8.GetBytes(content));
        }
        return (temp, VersionOf(content, utc));
    }

    // Received from a PC and committed, the way the sync does it.
    private string Commit(string turma, string path, string content = "x", bool replace = false, DateTime? utc = null)
    {
        var (temp, version) = Incoming(turma, content, utc);
        return _store.CommitIncoming(turma, temp, path, version, replace);
    }

    private void MakeJunction(string link, string target)
    {
        Directory.CreateDirectory(target);
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), "mklink /J did not make the junction");
        _links.Add(link);
    }

    // --- Classes ---

    [Fact]
    public void Create_MakesTheNameSafe_AndRefusesACaseOnlyDuplicate()
    {
        var (ok, detail, turma) = _store.Create("  Turma: 3º A  ");

        Assert.True(ok);
        Assert.Equal(string.Empty, detail);
        Assert.Equal("Turma_ 3º A", turma);
        Assert.True(Directory.Exists(FolderOf("Turma_ 3º A")));

        var (again, againDetail, againTurma) = _store.Create("TURMA_ 3º a");

        Assert.False(again);
        Assert.Equal("Já existe uma turma com esse nome.", againDetail);
        Assert.Equal(string.Empty, againTurma);
        Assert.Equal(new[] { "Turma_ 3º A" }, _store.List().Select(c => c.Turma));
    }

    [Fact]
    public void Create_RefusesTheSameNameTwice()
    {
        MakeClass();

        var (ok, detail, _) = _store.Create(Turma);

        Assert.False(ok);
        Assert.Equal("Já existe uma turma com esse nome.", detail);
    }

    [Fact]
    public void Create_RefusesTooLong()
    {
        var (tooLong, detail, turma) = _store.Create(new string('a', ClassLesson.MaxTurmaLength + 1));

        Assert.False(tooLong);
        Assert.Equal("Nome longo demais: use até 60 letras.", detail);
        Assert.Equal(string.Empty, turma);
        Assert.False(_store.Create(new string('b', 300)).Ok);
        Assert.Empty(_store.List());

        var (fits, _, kept) = _store.Create(new string('a', ClassLesson.MaxTurmaLength));
        Assert.True(fits);
        Assert.Equal(ClassLesson.MaxTurmaLength, kept.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RefusesAnEmptyName(string requested)
    {
        var (ok, detail, _) = _store.Create(requested);

        Assert.False(ok);
        Assert.Equal("Escolha um nome para a turma.", detail);
    }

    [Fact]
    public void Create_RefusesAPolicyVariable()
    {
        // Chrome expands ${...} in the download folder it is given.
        var (ok, detail, _) = _store.Create("Turma ${user_home}");

        Assert.False(ok);
        Assert.Contains("${", detail);
        Assert.Empty(_store.List());
    }

    [Fact]
    public void Create_StartsFromAnEmptyIndex_WhateverAnEarlierClassOfThatNameLeft()
    {
        Directory.CreateDirectory(IndexRoot);
        File.WriteAllText(IndexFileOf(Turma), "{\"Deleted\":[\"velho.txt\"]}");

        MakeClass();

        Assert.False(_store.IsDeleted(Turma, "velho.txt"));
        Assert.False(File.Exists(IndexFileOf(Turma)));
    }

    [Fact]
    public void Rename_And_Delete()
    {
        MakeClass();
        MakeClass("3º B");
        Commit(Turma, "mapa.png");

        var (renamed, renamedDetail) = _store.Rename(Turma, "  3º C ");

        Assert.True(renamed, renamedDetail);
        Assert.Equal(new[] { "3º B", "3º C" }, _store.List().Select(c => c.Turma));
        Assert.True(File.Exists(Path.Combine(FolderOf("3º C"), "mapa.png")));
        Assert.False(Directory.Exists(FolderOf(Turma)));
        Assert.Equal(Today, _store.ArrivedOn("3º C", "mapa.png"));
        Assert.True(File.Exists(IndexFileOf("3º C")));
        Assert.False(File.Exists(IndexFileOf(Turma)));

        var (deleted, deletedDetail) = _store.Delete("3º C");

        Assert.True(deleted, deletedDetail);
        Assert.Equal(new[] { "3º B" }, _store.List().Select(c => c.Turma));
        Assert.False(Directory.Exists(FolderOf("3º C")));
        Assert.False(File.Exists(IndexFileOf("3º C")));
        Assert.Equal(new[] { FolderOf("3º C") }, _bin.Recycled);
        Assert.Equal("x", File.ReadAllText(Path.Combine(_bin.InBin(FolderOf("3º C")), "mapa.png")));
        Assert.Empty(Directory.GetFiles(IndexRoot, "*.json"));
    }

    [Fact]
    public void Rename_RefusesABadOrTakenName_AndAMissingClass()
    {
        MakeClass();
        MakeClass("3º B");

        Assert.Equal((false, "Já existe uma turma com esse nome."), _store.Rename(Turma, "3º b"));
        Assert.Equal((false, "Escolha um nome para a turma."), _store.Rename(Turma, "  "));
        Assert.Equal((false, "Nome longo demais: use até 60 letras."), _store.Rename(Turma, new string('a', 61)));
        Assert.False(_store.Rename(Turma, "x${y}").Ok);
        Assert.Equal((false, "Essa turma não existe mais."), _store.Rename("4º A", "4º B"));
        Assert.Equal(new[] { "3º A", "3º B" }, _store.List().Select(c => c.Turma));
    }

    [Fact]
    public void Rename_ToTheNameItAlreadyHas_ChangesNothing()
    {
        MakeClass();

        Assert.Equal((true, string.Empty), _store.Rename(Turma, Turma));
        Assert.True(Directory.Exists(FolderOf(Turma)));
    }

    [Fact]
    public void Rename_ChangingOnlyTheCase_Works()
    {
        MakeClass("turma a");
        Commit("turma a", "mapa.png");

        var (ok, detail) = _store.Rename("turma a", "Turma A");

        Assert.True(ok, detail);
        Assert.Equal(new[] { "Turma A" }, _store.List().Select(c => c.Turma));
        Assert.True(File.Exists(Path.Combine(FolderOf("Turma A"), "mapa.png")));
        Assert.Equal(Today, _store.ArrivedOn("Turma A", "mapa.png"));
        Assert.Equal(Today, new ClassStore(FilesRoot, IndexRoot, _bin, () => _now).ArrivedOn("Turma A", "mapa.png"));
    }

    [Fact]
    public void Delete_WhenTheRecycleBinRefuses_KeepsTheClassAndItsIndex()
    {
        var bin = new MovingRecycleBin(Path.Combine(_root, "lixeira2"), refused: Turma);
        var store = NewStore(bin);
        store.Create(Turma);
        File.WriteAllText(Path.Combine(FolderOf(Turma), "a.txt"), "x");
        store.RefreshTombstones(Turma);

        var (ok, detail) = store.Delete(Turma);

        Assert.False(ok);
        Assert.Equal("Não foi possível excluir a turma: o arquivo está em uso por outro programa", detail);
        Assert.True(File.Exists(Path.Combine(FolderOf(Turma), "a.txt")));
        Assert.True(File.Exists(IndexFileOf(Turma)));
    }

    [Fact]
    public void Delete_OfAMissingClass_SaysSo()
    {
        Assert.Equal((false, "Essa turma não existe mais."), _store.Delete(Turma));
    }

    [Fact]
    public void List_SummarisesEachClass_InNaturalOrder()
    {
        MakeClass("10º A");
        MakeClass(Turma);
        MakeClass("3º B");
        Commit(Turma, "a.txt", "12345");
        Commit(Turma, "sub/b.txt", "123");
        _now = _now.AddDays(1);
        Commit(Turma, "c.txt", "1");
        Write("3º B", "um.txt", "1");

        var list = _store.List();

        Assert.Equal(new[] { Turma, "3º B", "10º A" }, list.Select(c => c.Turma));
        Assert.Equal(new ClassSummary(Turma, 3, 9, DateOnly.FromDateTime(_now)), list[0]);
        // Never indexed: it counts as arriving today, as when it is first indexed.
        Assert.Equal(new ClassSummary("3º B", 1, 1, DateOnly.FromDateTime(_now)), list[1]);
        Assert.Equal(new ClassSummary("10º A", 0, 0, null), list[2]);
    }

    [Fact]
    public void List_LeavesOutWhatIsNotAUsableClass()
    {
        MakeClass();
        Directory.CreateDirectory(FolderOf(new string('z', ClassLesson.MaxTurmaLength + 1)));
        Directory.CreateDirectory(FolderOf("a${b}"));
        File.WriteAllText(Path.Combine(FilesRoot, "solto.txt"), "x");
        MakeJunction(FolderOf("atalho"), Outside);

        Assert.Equal(new[] { Turma }, _store.List().Select(c => c.Turma));
        Assert.False(_store.Exists("atalho"));
        Assert.False(_store.Exists("a${b}"));
        Assert.True(_store.Exists(Turma));
        Assert.False(_store.Exists("4º A"));
    }

    [Fact]
    public void List_WithNoClassesYet_IsEmpty()
    {
        Assert.Empty(_store.List());
    }

    [Fact]
    public void FreeBytes_IsWhatTheDiskHasLeft()
    {
        Assert.True(_store.FreeBytes() > 0);
        Assert.Equal(5L * 1024 * 1024 * 1024, ClassStore.LowSpaceBytes);
    }

    // --- The files ---

    [Fact]
    public void Files_SkipsHiddenAndLinks()
    {
        MakeClass();
        Write(Turma, "a.txt", "abc", new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc));
        Write(Turma, "sub/b.txt");
        File.SetAttributes(Write(Turma, "escondido.txt"), FileAttributes.Hidden);
        File.SetAttributes(Write(Turma, "sistema.txt"), FileAttributes.System);
        Write(Turma, "pasta oculta/c.txt");
        var hiddenFolder = Path.Combine(FolderOf(Turma), "pasta oculta");
        File.SetAttributes(hiddenFolder, File.GetAttributes(hiddenFolder) | FileAttributes.Hidden);
        File.WriteAllText(Path.Combine(Outside, "fora.txt"), "x");
        MakeJunction(Path.Combine(FolderOf(Turma), "atalho"), Outside);

        var files = _store.Files(Turma);

        Assert.Equal(new[] { "a.txt", "sub/b.txt" }, files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new FileVersion(3, new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc).Ticks), files["a.txt"]);
    }

    [Fact]
    public void Files_KeysAreCaseInsensitive()
    {
        MakeClass();
        Write(Turma, "Fotos/Mapa.PNG");

        var files = _store.Files(Turma);

        Assert.True(files.ContainsKey("fotos/mapa.png"));
        Assert.True(files.TryGetValue("FOTOS/MAPA.PNG", out var version));
        Assert.Equal(1, version.Size);
    }

    [Fact]
    public void Files_LeavesOutWhatTheStudentSideWouldNotList()
    {
        MakeClass();
        Write(Turma, "bom.txt");
        Write(Turma, "~$trabalho.docx");
        Write(Turma, "baixando.crdownload");
        Write(Turma, "rascunho.TMP");
        Write(Turma, "meio.part");
        Write(Turma, ".midview-abc.parcial");
        Write(Turma, ".midview-qualquer.dat"); // Midview's own prefix, whatever follows
        Write(Turma, new string('n', FileNames.MaxNameLength + 1) + ".txt"); // a name Safe would shorten
        Write(Turma, "a/b/c/d/e/f/g/oito.txt"); // 8 segments: the file name counts
        Write(Turma, "a/b/c/d/e/f/g/h/nove.txt"); // 9 segments

        Assert.Equal(new[] { "a/b/c/d/e/f/g/oito.txt", "bom.txt" }, _store.Files(Turma).Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Files_LeavesOutAPathTooLongForTheWire()
    {
        MakeClass();
        var segment = new string('ã', 90); // 180 bytes of UTF-8 each
        Write(Turma, string.Join('/', Enumerable.Repeat(segment, 6)) + "/x.txt"); // over 1000 bytes
        Write(Turma, "curto.txt");

        Assert.Equal(new[] { "curto.txt" }, _store.Files(Turma).Keys);
    }

    [Fact]
    public void Files_LeavesOutAFileTooBigToSend()
    {
        MakeClass();
        using (var stream = new FileStream(Path.Combine(FolderOf(Turma), "enorme.bin"), FileMode.Create))
        {
            stream.SetLength(FileTransfer.MaxFileSize + 1);
        }
        Write(Turma, "normal.txt");

        Assert.Equal(new[] { "normal.txt" }, _store.Files(Turma).Keys);
    }

    [Fact]
    public void Files_OfAClassThatIsNotThere_IsEmpty()
    {
        Assert.Empty(_store.Files("4º A"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("C:")]
    [InlineData("")]
    [InlineData("a${b}")]
    public void ANameThatIsNoClassName_IsRefusedByEveryMethodThatTakesOne(string turma)
    {
        Assert.Throws<ArgumentException>(() => _store.Files(turma));
        Assert.Throws<ArgumentException>(() => _store.PathOf(turma, "a.txt"));
        Assert.Throws<ArgumentException>(() => _store.BeginIncoming(turma));
        Assert.Throws<ArgumentException>(() => _store.RefreshTombstones(turma));
        Assert.Throws<ArgumentException>(() => _store.EndLesson(turma, EndChoice.DeleteAll));
    }

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
}
