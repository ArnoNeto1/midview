using Midview.Agent;
using Xunit;

public class SiteBlockStateTests
{
    [Fact]
    public void Load_NoExistingFile_ReturnsEmptyDefaultState()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.json");

        var state = SiteBlockState.Load(tempPath);

        Assert.False(state.InternetBlocked);
        Assert.Empty(state.BlockedDomains);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsState()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.json");
        try
        {
            var state = new SiteBlockState { InternetBlocked = true };
            state.BlockedDomains.Add("youtube.com");
            state.Save(tempPath);

            var loaded = SiteBlockState.Load(tempPath);

            Assert.True(loaded.InternetBlocked);
            Assert.Contains("youtube.com", loaded.BlockedDomains);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Load_CorruptFile_StartsEmptyAndKeepsTheFileAside()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempPath, "{\"InternetBlocked\": tr"); // what a power cut mid-write leaves behind

            var state = SiteBlockState.Load(tempPath);

            Assert.False(state.InternetBlocked);
            Assert.Empty(state.BlockedDomains);
            Assert.False(File.Exists(tempPath));
            Assert.Equal("{\"InternetBlocked\": tr", File.ReadAllText(tempPath + ".corrupt"));
        }
        finally
        {
            File.Delete(tempPath);
            File.Delete(tempPath + ".corrupt");
        }
    }

    [Fact]
    public void Load_EmptyFile_StartsEmpty()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempPath, string.Empty);

            var state = SiteBlockState.Load(tempPath);

            Assert.False(state.InternetBlocked);
        }
        finally
        {
            File.Delete(tempPath);
            File.Delete(tempPath + ".corrupt");
        }
    }

    [Fact]
    public void Save_OverExistingFile_ReplacesItAndLeavesNoTempFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.json");
        try
        {
            new SiteBlockState { InternetBlocked = true }.Save(tempPath);
            new SiteBlockState { InternetBlocked = false }.Save(tempPath);

            Assert.False(SiteBlockState.Load(tempPath).InternetBlocked);
            Assert.False(File.Exists(tempPath + ".tmp"));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
