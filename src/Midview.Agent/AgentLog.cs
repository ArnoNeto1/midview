namespace Midview.Agent;

public static class AgentLog
{
    public static void Warn(string message)
    {
        var programDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Midview");
        Directory.CreateDirectory(programDataDir);
        var logPath = Path.Combine(programDataDir, "agent.log");
        File.AppendAllText(logPath, $"{DateTimeOffset.UtcNow:O} WARN {message}{Environment.NewLine}");
    }
}
