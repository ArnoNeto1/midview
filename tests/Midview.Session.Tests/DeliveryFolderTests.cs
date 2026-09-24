using Midview.Protocol;
using Midview.Session;
using Xunit;

public class DeliveryFolderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"midview-delivery-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void NoFolderYet_HasNothingToCollect()
    {
        Assert.Empty(new DeliveryFolder(_directory).ListFiles());
    }

    [Fact]
    public void TryCreate_MakesTheFolder()
    {
        new DeliveryFolder(_directory).TryCreate();

        Assert.True(Directory.Exists(_directory));
    }

    [Fact]
    public void ListFiles_IncludesSubfolders_InPathOrder()
    {
        Write("b.txt");
        Write("A.txt");
        Write("site/index.html");
        Write("site/imagens/logo.png");

        var files = new DeliveryFolder(_directory).ListFiles();

        Assert.Equal(new[] { "A.txt", "b.txt", "site/index.html", "site/imagens/logo.png" }, files.Select(file => file.RelativePath));
        Assert.Equal(Path.Combine(_directory, "site", "index.html"), files[2].FullPath);
    }

    [Fact]
    public void ListFiles_SkipsHiddenFiles()
    {
        Write("trabalho.docx");
        // Dot-prefixed too, so it's hidden wherever the test runs.
        File.SetAttributes(Write(".~lock.trabalho.docx"), FileAttributes.Hidden);

        Assert.Equal(new[] { "trabalho.docx" }, new DeliveryFolder(_directory).ListFiles().Select(file => file.RelativePath));
    }

    [Fact]
    public void ListFiles_StopsAtTheMaximumDepth()
    {
        var deep = string.Join("/", Enumerable.Range(1, FileCollection.MaxDepth + 2).Select(level => $"n{level}"));
        Write($"{deep}/fundo.txt");
        var deepest = string.Join("/", Enumerable.Range(1, FileCollection.MaxDepth - 1).Select(level => $"n{level}")) + "/ok.txt";
        Write(deepest);

        Assert.Equal(new[] { deepest }, new DeliveryFolder(_directory).ListFiles().Select(file => file.RelativePath));
    }

    [Fact]
    public void ListFiles_StopsJustPastTheFileLimit()
    {
        for (var i = 0; i < 10; i++)
        {
            Write($"f{i}.txt");
        }

        Assert.Equal(4, new DeliveryFolder(_directory, maxFiles: 3).ListFiles().Count);
    }

    [Fact]
    public void ListFiles_SkipsLinkedFolders()
    {
        var elsewhere = Path.Combine(_directory + "-fora");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "segredo.txt"), "x");
        Write("meu.txt");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(_directory, "atalho"), elsewhere);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return; // Windows without Developer Mode can't create one; nothing to check then
            }

            Assert.Equal(new[] { "meu.txt" }, new DeliveryFolder(_directory).ListFiles().Select(file => file.RelativePath));
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    private string Write(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, relativePath);
        return path;
    }
}
