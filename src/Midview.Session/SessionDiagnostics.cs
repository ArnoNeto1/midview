namespace Midview.Session;

public static class SessionDiagnostics
{
    // Written ~15 times a second while a Master is viewing, so it's capped:
    // past this size the current file becomes session-perf.log.old
    // (replacing any previous one) and a new file starts.
    private const long MaxLogBytes = 5 * 1024 * 1024;

    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "session-perf.log");
    private static readonly object Lock = new();

    // Never throws: this is called from inside the capture loop, and a log
    // that can't be written (a read-only install folder for a standard
    // user, a locked file) must never stop the screen stream.
    public static void Log(string message)
    {
        var line = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
        lock (Lock)
        {
            try
            {
                var logFile = new FileInfo(LogPath);
                if (logFile.Exists && logFile.Length > MaxLogBytes)
                {
                    File.Move(LogPath, LogPath + ".old", overwrite: true);
                }
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
                // Diagnostics only; nothing else to do.
            }
        }
    }
}
