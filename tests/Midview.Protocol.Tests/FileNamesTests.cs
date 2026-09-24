using Midview.Protocol;
using Xunit;

public class FileNamesTests
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
    public void Safe_KeepsOnlyAPlainName(string requested, string expected)
    {
        Assert.Equal(expected, FileNames.Safe(requested));
    }

    [Fact]
    public void Safe_TooLong_IsShortenedKeepingTheExtension()
    {
        var name = FileNames.Safe(new string('a', 300) + ".pdf");

        Assert.Equal(FileNames.MaxNameLength, name.Length);
        Assert.EndsWith(".pdf", name);
    }

    [Fact]
    public void MoveToUniqueName_NeverOverwritesAFileOrAFolder()
    {
        var directory = Directory.CreateTempSubdirectory("midview-names-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "notas.pdf"), "antigo");
            Directory.CreateDirectory(Path.Combine(directory, "notas (2).pdf"));
            var temp = Path.Combine(directory, "temp");
            File.WriteAllText(temp, "novo");

            Assert.Equal("notas (3).pdf", FileNames.MoveToUniqueName(temp, directory, "notas.pdf"));
            Assert.Equal("antigo", File.ReadAllText(Path.Combine(directory, "notas.pdf")));
            Assert.Equal("novo", File.ReadAllText(Path.Combine(directory, "notas (3).pdf")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
