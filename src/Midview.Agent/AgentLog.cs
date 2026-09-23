namespace Midview.Agent;

public static class AgentLog
{
    // Never throws: it's called from catch blocks and startup error paths,
    // where a logging failure (locked file, missing directory) must not turn
    // into a second, unhandled error that stops the service.
    public static void Warn(string message)
    {
        try
        {
            Directory.CreateDirectory(AgentDataDirectory.DirectoryPath);
            var logPath = Path.Combine(AgentDataDirectory.DirectoryPath, "agent.log");
            File.AppendAllText(logPath, $"{DateTimeOffset.UtcNow:O} WARN {message}{Environment.NewLine}");
        }
        catch
        {
            // Nowhere left to report it.
        }
    }
}
