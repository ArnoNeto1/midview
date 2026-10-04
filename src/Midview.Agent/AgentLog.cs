namespace Midview.Agent;

public static class AgentLog
{
    // The log is kept to about twice this size: when it grows past it, it
    // becomes agent.log.1 (replacing the previous one) and a new one starts.
    // A program that is ended again and again must not fill the disk.
    public const long MaxBytes = 5L * 1024 * 1024;

    private static readonly object Gate = new();

    // Never throws: it's called from catch blocks and startup error paths,
    // where a logging failure (locked file, missing directory) must not turn
    // into a second, unhandled error that stops the service.
    public static void Warn(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AgentDataDirectory.DirectoryPath);
                var logPath = Path.Combine(AgentDataDirectory.DirectoryPath, "agent.log");
                RotateIfTooBig(logPath);
                File.AppendAllText(logPath, $"{DateTimeOffset.UtcNow:O} WARN {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Nowhere left to report it.
        }
    }

    public static void RotateIfTooBig(string logPath, long maxBytes = MaxBytes)
    {
        try
        {
            var info = new FileInfo(logPath);
            if (info.Exists && info.Length > maxBytes)
            {
                File.Move(logPath, logPath + ".1", overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Rotation is housekeeping: the line is still appended.
        }
    }
}
