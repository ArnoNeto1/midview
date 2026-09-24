using Midview.Session;
using Xunit;

public class FileInboxTests
{
    [Theory]
    [InlineData("notas.pdf", "notas.pdf")]
    [InlineData(@"..\..\Windows\System32\evil.exe", "evil.exe")]
    [InlineData("../../etc/evil.sh", "evil.sh")]
    [InlineData(@"C:\Users\Public\x.txt", "x.txt")]
    [InlineData("arquivo.txt:escondido", "arquivo.txt_escondido")]
    [InlineData("a<b>c|d?e*f\"g.txt", "a_b_c_d_e_f_g.txt")]
    [InlineData("relatório final.docx. . ", "relatório final.docx")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("COM1.log", "_COM1.log")]
    [InlineData("", "arquivo")]
    [InlineData("...", "arquivo")]
    [InlineData(@"pasta\", "arquivo")]
    public void SafeFileName_KeepsOnlyAPlainNameInsideTheInbox(string requested, string expected)
    {
        Assert.Equal(expected, FileInbox.SafeFileName(requested));
    }

    [Fact]
    public void SafeFileName_TooLong_IsShortenedKeepingTheExtension()
    {
        var name = FileInbox.SafeFileName(new string('a', 300) + ".pdf");

        Assert.Equal(FileInbox.MaxNameLength, name.Length);
        Assert.EndsWith(".pdf", name);
    }

    [Fact]
    public void Complete_SavesUnderTheSafeName_AndNeverOverwrites()
    {
        using var directory = new TempDirectory();
        var inbox = new FileInbox(directory.Path);

        var first = Save(inbox, "notas.pdf", new byte[] { 1 });
        var second = Save(inbox, "notas.pdf", new byte[] { 2 });
        var third = Save(inbox, "notas.pdf", new byte[] { 3 });

        Assert.Equal(new[] { "notas.pdf", "notas (2).pdf", "notas (3).pdf" }, new[] { first, second, third });
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(directory.Path, "notas.pdf")));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(Path.Combine(directory.Path, "notas (3).pdf")));
    }

    [Fact]
    public void NotCompleted_LeavesNothingBehind()
    {
        using var directory = new TempDirectory();
        var inbox = new FileInbox(directory.Path);

        using (var incoming = inbox.Begin("pela-metade.zip"))
        {
            incoming.Write(new byte[] { 1, 2, 3 });
        }

        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public void RefusalFor_ASizeThatFits_IsNull()
    {
        using var directory = new TempDirectory();

        Assert.Null(new FileInbox(directory.Path).RefusalFor(1024));
    }

    private static string Save(FileInbox inbox, string name, byte[] content)
    {
        using var incoming = inbox.Begin(name);
        incoming.Write(content);
        return incoming.Complete();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"midview-inbox-{Guid.NewGuid():N}");

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
