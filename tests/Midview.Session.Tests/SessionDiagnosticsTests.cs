using Midview.Session;
using Xunit;

public class SessionDiagnosticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-perf-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void TheLog_GoesToTheStudentsOwnAppData_NotTheInstallFolder()
    {
        Assert.Equal(
            Path.Combine(@"C:\Users\aluno\AppData\Local", "Midview", "session-perf.log"),
            SessionDiagnostics.PathFor(@"C:\Users\aluno\AppData\Local", @"C:\Program Files\Midview"));
    }

    [Fact]
    public void WithNoProfileFolder_TheLog_GoesNextToTheExe()
    {
        Assert.Equal(
            Path.Combine(@"C:\Program Files\Midview", "session-perf.log"),
            SessionDiagnostics.PathFor("", @"C:\Program Files\Midview"));
    }

    [Fact]
    public void Append_CreatesTheFolder_TheFirstTime()
    {
        var logPath = Path.Combine(_root, "Midview", "session-perf.log");

        SessionDiagnostics.Append(logPath, "first line");
        SessionDiagnostics.Append(logPath, "second line");

        Assert.Equal(new[] { "first line", "second line" }, File.ReadAllLines(logPath));
    }

    [Fact]
    public void Append_DoesNotThrow_WhenTheLogCantBeWritten()
    {
        // A file where the log's folder should be: nothing can be created there.
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "Midview");
        File.WriteAllText(blocker, "not a folder");

        var exception = Record.Exception(() =>
            SessionDiagnostics.Append(Path.Combine(blocker, "session-perf.log"), "must not stop the screen stream"));

        Assert.Null(exception);
    }
}
