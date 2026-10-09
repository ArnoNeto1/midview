using Midview.Agent;
using Xunit;

// Against real files under %TEMP%: the key is read from a file on disk.
public class FileChangeKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"midview-files-{Guid.NewGuid():N}");

    public FileChangeKeyTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, byte fill, int length)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, Fill(fill, length));
        return path;
    }

    private static byte[] Fill(byte value, int length)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, value);
        return bytes;
    }

    private static FileChangeKey KeyOf(string path) => FileChangeKey.From(path, new FileInfo(path))!.Value;

    [Fact]
    public void From_IsStableForAFileThatHasNotChanged()
    {
        var path = Write("app.exe", (byte)'a', 50_000);

        Assert.Equal(KeyOf(path), KeyOf(path));
    }

    // The attack: a file's owner swaps in a different program, pads it to the
    // same length, and sets its write and creation times back -- all of which
    // they may do to their own file. The key must still tell the two apart, or
    // the facts read for the first file would be reused for the second.
    [Fact]
    public void From_TellsApartADifferentFileOfTheSameLengthWithItsTimesSetBack()
    {
        var path = Write("app.exe", (byte)'a', 50_000);
        var info = new FileInfo(path);
        var written = info.LastWriteTimeUtc;
        var created = info.CreationTimeUtc;
        var before = KeyOf(path);

        File.WriteAllBytes(path, Fill((byte)'b', 50_000));
        File.SetLastWriteTimeUtc(path, written);
        File.SetCreationTimeUtc(path, created);
        var after = KeyOf(path);

        Assert.NotEqual(before, after);
    }
}
