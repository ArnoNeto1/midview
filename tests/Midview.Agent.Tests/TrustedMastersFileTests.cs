using Midview.Agent;
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
}
