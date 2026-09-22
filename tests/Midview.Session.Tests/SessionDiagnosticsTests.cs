using Midview.Session;
using Xunit;

public class SessionDiagnosticsTests
{
    [Fact]
    public void Log_DoesNotThrow_WhenTheLogFileIsHeldExclusivelyByAnotherHandle()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "session-perf.log");
        using var exclusiveHandle = new FileStream(
            logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var exception = Record.Exception(() => SessionDiagnostics.Log("connection and capture paths must survive a blocked log write"));

        Assert.Null(exception);
    }
}
