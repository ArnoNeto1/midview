namespace Midview.Agent;

public static class AgentLog
{
    // Never throws: it's called from catch blocks and startup paths that must
    // keep going, so a failed log write (directory not created yet, file
    // locked by a reader) is dropped instead of becoming a second failure.
    // Doesn't touch the directory's ACL either -- EnsureSecure now reapplies
    // it in full, which is startup work, not something to repeat per line.
    public static void Warn(string message)
    {
        try
        {
            var logPath = Path.Combine(AgentDataDirectory.DirectoryPath, "agent.log");
            File.AppendAllText(logPath, $"{DateTimeOffset.UtcNow:O} WARN {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // See above: nowhere left to report a logging failure.
        }
    }
}
