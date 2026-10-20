using Midview.Protocol;
using Midview.Session;
using Xunit;

public class ClassFolderTests : IDisposable
{
    private static readonly long Monday = new DateTime(2026, 10, 19, 13, 0, 0, DateTimeKind.Utc).Ticks;

    private readonly string _work = Path.Combine(Path.GetTempPath(), $"midview-classfolder-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    private static readonly DateTime Now = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);

    private string FolderPath => Path.Combine(_work, "Desktop", "Pasta da turma");

    private string BinPath => Path.Combine(_work, "Lixeira");

    private ClassFolder Folder() =>
        new(FolderPath, Path.Combine(_work, "LocalAppData", "pasta-da-turma.txt"), new FakeRecycleBin(BinPath));

    [Fact]
    public void ANewLesson_RecyclesWhatWasThere_ButKeepsItsOwnFilesAlready()
    {
        var folder = Folder();
        var kept = Write("redação.docx", 3, Monday);
        Write("da outra turma.txt", 1, Monday);
        Write("outra/pasta/foto.jpg", 2, Monday);
        File.SetAttributes(Write(".escondido", 1, Monday), FileAttributes.Hidden);
        var lesson = Guid.NewGuid();
        var manifest = new[]
        {
            new ClassFolderEntry("redação.docx", 3, Monday),
            new ClassFolderEntry("novo.txt", 5, Monday),
        };

        var preparation = folder.Prepare(lesson, "7º A", manifest, Now);

        Assert.Equal(new[] { 1 }, preparation.Wanted);
        Assert.Empty(preparation.Problems);
        Assert.True(preparation.NewLesson);
        Assert.Equal(new[] { kept }, Directory.GetFileSystemEntries(FolderPath, "*", SearchOption.AllDirectories));
        Assert.Equal(new[] { ".escondido", "da outra turma.txt", "outra" }, Directory.GetFileSystemEntries(BinPath).Select(Path.GetFileName).Order());
        Assert.Equal(new ClassFolderLesson(lesson, "7º A", Now), folder.CurrentLesson());
    }

    [Fact]
    public void TheSameLessonAgain_DeletesNothing_AndOnlyAsksForWhatsMissing()
    {
        var folder = Folder();
        var lesson = Guid.NewGuid();
        folder.Prepare(lesson, "7º A", Array.Empty<ClassFolderEntry>(), Now);
        Write("trabalho ainda não salvo.docx", 4, Monday);
        Write("notas.txt", 9, Monday + 1); // changed by the student

        var preparation = folder.Prepare(lesson, "7º A", new[]
        {
            new ClassFolderEntry("notas.txt", 1, Monday),
            new ClassFolderEntry("faltando.txt", 1, Monday),
        }, Now.AddMinutes(1));

        Assert.Equal(new[] { 1 }, preparation.Wanted);
        Assert.False(preparation.NewLesson);
        Assert.Equal(2, folder.ListFiles().Count);
    }

    [Fact]
    public void AnotherLessonStillGoingOn_IsLeftAlone_UntilItsBeenQuietAWhile()
    {
        var folder = Folder();
        var first = Guid.NewGuid();
        folder.Prepare(first, "7º A", Array.Empty<ClassFolderEntry>(), Now);
        Write("trabalho.docx", 1, Monday);

        var refused = folder.Prepare(Guid.NewGuid(), "8º B", Array.Empty<ClassFolderEntry>(), Now.AddMinutes(4));

        Assert.Contains("turma 7º A", refused.Refusal);
        Assert.Single(folder.ListFiles());
        Assert.Equal(first, folder.CurrentLesson()!.Id);

        var second = Guid.NewGuid();
        var replaced = folder.Prepare(second, "8º B", Array.Empty<ClassFolderEntry>(), Now.AddMinutes(4) + ClassFolder.ActiveLessonWindow);

        Assert.Null(replaced.Refusal);
        Assert.Empty(folder.ListFiles());
        Assert.Equal(second, folder.CurrentLesson()!.Id);
    }

    [Fact]
    public void AFileFromTheTeacher_ReplacesTheOldOne_WithTheTeachersTime()
    {
        var folder = Folder();
        Write("notas.txt", 1, Monday);
        using (var incoming = folder.Begin("notas.txt", Monday + 5))
        {
            incoming.Write(new byte[] { 7, 8 });
            incoming.Complete();
        }

        var file = Assert.Single(folder.ListFiles());
        Assert.Equal(new ClassFolderEntry("notas.txt", 2, Monday + 5), file.Entry);
        Assert.Equal(new byte[] { 7, 8 }, File.ReadAllBytes(file.FullPath));
    }

    [Fact]
    public void AFileThatDoesntComplete_LeavesNothingBehind()
    {
        var folder = Folder();
        using (var incoming = folder.Begin("site/novo.txt", Monday))
        {
            incoming.Write(new byte[] { 1 });
        }

        Assert.Empty(Directory.GetFiles(FolderPath, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("../fora.txt")]
    [InlineData("pasta/../../fora.txt")]
    [InlineData("C:/fora.txt")]
    public void APathOutsideTheFolder_IsRefused(string path)
    {
        Assert.Throws<IOException>(() => Folder().Begin(path, Monday));
        Assert.False(File.Exists(Path.Combine(_work, "fora.txt")));
    }

    [Fact]
    public void Clear_RecyclesEverything_AndForgetsTheLesson()
    {
        var folder = Folder();
        folder.Prepare(Guid.NewGuid(), "7º A", Array.Empty<ClassFolderEntry>(), Now);
        Write("a.txt", 1, Monday);
        Write("site/b.txt", 1, Monday);

        var (recycled, problems) = folder.Clear();

        Assert.Equal(2, recycled);
        Assert.Empty(problems);
        Assert.True(Directory.Exists(FolderPath));
        Assert.Empty(Directory.GetFileSystemEntries(FolderPath));
        Assert.True(File.Exists(Path.Combine(BinPath, "site", "b.txt")));
        Assert.Null(folder.CurrentLesson());
    }

    private string Write(string relativePath, int size, long ticks)
    {
        var path = Path.Combine(new[] { FolderPath }.Concat(relativePath.Split('/')).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, new DateTime(ticks, DateTimeKind.Utc));
        return path;
    }
}
