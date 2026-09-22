namespace Midview.Agent;

public static class AgentLog
{
    public static void Warn(string message)
    {
        var programDataDir = AgentDataDirectory.EnsureSecure();
        var logPath = Path.Combine(programDataDir, "agent.log");
        File.AppendAllText(logPath, $"{DateTimeOffset.UtcNow:O} WARN {message}{Environment.NewLine}");
    }
}
