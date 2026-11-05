using Midview.Session;
using Xunit;

public class WindowsShellFoldersTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"midview-shell-{Guid.NewGuid():N}");

    public WindowsShellFoldersTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void IdListOf_ATempFolder_EndsInZeroAndDecodesBack()
    {
        var shell = new WindowsShellFolders();
        var bytes = shell.IdListOf(_folder);

        Assert.NotNull(bytes);
        Assert.True(bytes!.Length >= 2);
        Assert.Equal(0, bytes[^1]);
        Assert.Equal(0, bytes[^2]);
        Assert.Equal(Normalize(_folder), Normalize(shell.PathOf(bytes)!));
    }

    // A student can write anything into the dialog's history, so bytes that are
    // not a whole ID list are never handed to the shell.
    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 0x10, 0, 1, 2 })]          // an item longer than the bytes
    [InlineData(new byte[] { 1, 0, 0, 0 })]             // an item too short to hold its own size
    [InlineData(new byte[] { 4, 0, 1, 2 })]             // no terminating zero
    public void PathOf_NotAnIdList_IsNull(byte[] bytes)
    {
        Assert.Null(new WindowsShellFolders().PathOf(bytes));
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
}
