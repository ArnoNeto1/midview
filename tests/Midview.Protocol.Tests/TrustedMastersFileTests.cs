using Midview.Protocol;
using Xunit;

public class TrustedMastersFileTests
{
    [Fact]
    public void Load_NoFile_ReturnsEmptyList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");

        var result = TrustedMastersFile.Load(path);

        Assert.Empty(result);
    }

    [Fact]
    public void Load_ExistingFile_ReturnsThumbprintList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "[\"ABCD1234\", \"EFGH5678\"]");

            var result = TrustedMastersFile.Load(path);

            Assert.Equal(new[] { "ABCD1234", "EFGH5678" }, result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Add_NoFile_CreatesTheList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            var result = TrustedMastersFile.Add(path, "abcd1234");

            Assert.Equal(new[] { "ABCD1234" }, result);
            Assert.Equal(new[] { "ABCD1234" }, TrustedMastersFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Add_KeepsTheOthers_AndNeverDuplicates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "[\"aaaa\", \"BBBB\"]");

            Assert.Equal(new[] { "AAAA", "BBBB" }, TrustedMastersFile.Add(path, " bbbb "));
            Assert.Equal(new[] { "AAAA", "BBBB", "CCCC" }, TrustedMastersFile.Add(path, "CCCC"));
            Assert.Equal(new[] { "AAAA", "BBBB", "CCCC" }, TrustedMastersFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Add_AListThatCantBeRead_IsLeftAlone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "isto não é json");

            Assert.Throws<InvalidDataException>(() => TrustedMastersFile.Add(path, "CCCC"));
            Assert.Equal("isto não é json", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
