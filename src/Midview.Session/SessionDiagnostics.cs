namespace Midview.Session;

public static class SessionDiagnostics
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "session-perf.log");
    private static readonly object Lock = new();

    // Never throws: it's called from HandleClient's receive loop and from
    // the capture thread, both of which must keep running a connection
    // that a blocked or unwritable log file (locked by a reader, an
    // install folder Users can't write to) would otherwise tear down. A
    // failed log write is dropped instead of becoming a second failure,
    // same as AgentLog.Warn.
    public static void Log(string message)
    {
        var line = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
        try
        {
            lock (Lock)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // See above: nowhere left to report a logging failure.
        }
    }
}
