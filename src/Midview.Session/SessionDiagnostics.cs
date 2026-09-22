namespace Midview.Session;

public static class SessionDiagnostics
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "session-perf.log");
    private static readonly object Lock = new();

    public static void Log(string message)
    {
        var line = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
        lock (Lock)
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
    }
}
