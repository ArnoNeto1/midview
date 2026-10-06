using Midview.Protocol;
using Xunit;

// Against the real Recycle Bin of whoever runs the tests: each file is named
// so it's recognizable there.
public class WindowsRecycleBinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-lixeira-{Guid.NewGuid():N}");

    public WindowsRecycleBinTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void AFileAndAFolder_LeaveForTheRecycleBin()
    {
        var file = Path.Combine(_root, "midview-teste-lixeira.txt");
        File.WriteAllText(file, "pode apagar");
        var folder = Path.Combine(_root, "midview-teste-lixeira");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "dentro.txt"), "pode apagar");

        var bin = new WindowsRecycleBin();
        bin.Recycle(file);
        bin.Recycle(folder);

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void AFileInUse_StaysAndSaysWhy()
    {
        var file = Path.Combine(_root, "midview-teste-em-uso.txt");
        File.WriteAllText(file, "aberto");
        using var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.ThrowsAny<IOException>(() => new WindowsRecycleBin().Recycle(file));
        Assert.True(File.Exists(file));
    }
}
