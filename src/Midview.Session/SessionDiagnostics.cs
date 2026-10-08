namespace Midview.Session;

public static class SessionDiagnostics
{
    // Written on every capture while a Master is viewing (15 to 30 times a
    // second, see CaptureCadence), so it's capped:
    // past this size the current file becomes session-perf.log.old
    // (replacing any previous one) and a new file starts.
    private const long MaxLogBytes = 5 * 1024 * 1024;

    // In the student's own AppData, since Session runs as the student. Next
    // to the exe it sat in the install folder, which a standard user can't
    // write to, so on a lab machine nothing was ever logged.
    public static string LogPath { get; } = PathFor(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppContext.BaseDirectory);

    private static readonly object Lock = new();

    // Next to the exe only when there's no profile folder to use.
    public static string PathFor(string localAppData, string exeDirectory) =>
        string.IsNullOrEmpty(localAppData)
            ? Path.Combine(exeDirectory, "session-perf.log")
            : Path.Combine(localAppData, "Midview", "session-perf.log");

    public static void Log(string message) => Append(LogPath, $"{DateTime.UtcNow:HH:mm:ss.fff} {message}");

    // Never throws: this is called from inside the capture loop, and a log
    // that can't be written (a folder that can't be created, a locked
    // file) must never stop the screen stream.
    public static void Append(string logPath, string line)
    {
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                var logFile = new FileInfo(logPath);
                if (logFile.Exists && logFile.Length > MaxLogBytes)
                {
                    File.Move(logPath, logPath + ".old", overwrite: true);
                }
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch
            {
                // Diagnostics only; nothing else to do.
            }
        }
    }
}
