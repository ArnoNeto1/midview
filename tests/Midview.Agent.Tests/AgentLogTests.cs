using Midview.Agent;
using Xunit;

public class AgentLogTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-agentlog-{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        File.Delete(_path);
        File.Delete(_path + ".1");
    }

    [Fact]
    public void ASmallLog_IsLeftAlone()
    {
        File.WriteAllText(_path, "abc");

        AgentLog.RotateIfTooBig(_path, maxBytes: 100);

        Assert.True(File.Exists(_path));
        Assert.False(File.Exists(_path + ".1"));
    }

    [Fact]
    public void ABigLog_BecomesTheOldOne_ReplacingThePreviousOldOne()
    {
        File.WriteAllText(_path + ".1", "very old");
        File.WriteAllText(_path, new string('x', 200));

        AgentLog.RotateIfTooBig(_path, maxBytes: 100);

        Assert.False(File.Exists(_path));
        Assert.Equal(200, File.ReadAllText(_path + ".1").Length);
    }

    [Fact]
    public void AMissingLog_IsFine() => AgentLog.RotateIfTooBig(_path, maxBytes: 100);
}
