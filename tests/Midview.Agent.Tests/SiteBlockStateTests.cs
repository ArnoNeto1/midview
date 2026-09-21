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
}
