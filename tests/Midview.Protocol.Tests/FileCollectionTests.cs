using Midview.Protocol;
using Xunit;

public class FileCollectionTests
{
    [Theory]
    [InlineData("trabalho.docx", new[] { "trabalho.docx" })]
    [InlineData("site/imagens/logo.png", new[] { "site", "imagens", "logo.png" })]
    [InlineData(@"site\index.html", new[] { "site", "index.html" })]
    [InlineData("../../../Windows/System32/evil.exe", new[] { "Windows", "System32", "evil.exe" })]
    [InlineData("a/./../b/./c.txt", new[] { "a", "b", "c.txt" })]
    [InlineData("C:/Users/Professor/x.txt", new[] { "C_", "Users", "Professor", "x.txt" })]
    [InlineData("//servidor/compartilhado/x.txt", new[] { "servidor", "compartilhado", "x.txt" })]
    [InlineData("pasta/CON/nul.txt", new[] { "pasta", "_CON", "_nul.txt" })]
    [InlineData("dados.txt:escondido", new[] { "dados.txt_escondido" })]
    [InlineData(" espaço /arquivo. ", new[] { "espaço", "arquivo" })]
    [InlineData("../..", new[] { "arquivo" })]
    [InlineData("", new[] { "arquivo" })]
    public void SafeRelativePath_NeverClimbsOut(string requested, string[] expected)
    {
        Assert.Equal(expected, FileCollection.SafeRelativePath(requested));
    }

    [Fact]
    public void SafeRelativePath_TooDeep_KeepsTheLastSegments()
    {
        var deep = string.Join("/", Enumerable.Range(1, 20).Select(i => $"n{i}")) + "/fim.txt";

        var segments = FileCollection.SafeRelativePath(deep);

        Assert.Equal(FileCollection.MaxDepth, segments.Count);
        Assert.Equal("fim.txt", segments[^1]);
        Assert.Equal("n14", segments[0]);
    }
}
