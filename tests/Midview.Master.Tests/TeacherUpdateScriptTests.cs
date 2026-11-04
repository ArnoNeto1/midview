using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Midview.Master;
using Xunit;

// The script TeacherUpdate leaves to finish an update once the Midview has
// closed, run for real by Windows PowerShell, with a made-up setup and a
// made-up Midview (.cmd files that write down how they were called).
public sealed class TeacherUpdateScriptTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"midview-aplicar-{Guid.NewGuid():N}");
    private readonly string _setup;
    private readonly string _program;
    private readonly string _setupCalls;
    private readonly string _programCalls;
    private readonly string _log;

    public TeacherUpdateScriptTests()
    {
        Directory.CreateDirectory(_folder);
        _setupCalls = Path.Combine(_folder, "setup-chamado.txt");
        _programCalls = Path.Combine(_folder, "programa-chamado.txt");
        _log = Path.Combine(_folder, "instalacao-0.0.38.log");
        _setup = Path.Combine(_folder, "MidviewSetup-0.0.38.cmd");
        _program = Path.Combine(_folder, "Midview Master.cmd");
        File.WriteAllText(_program, $"@echo %*> \"{_programCalls}\"\r\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private void SetupThatEnds(int exitCode) =>
        File.WriteAllText(_setup, $"@echo %*> \"{_setupCalls}\"\r\n@exit /b {exitCode}\r\n");

    private static string Sha256Of(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private int Run(string sha256, int waitFor)
    {
        var script = TeacherUpdate.WriteApplyScript(_folder);
        var info = TeacherUpdate.ApplyStartInfo(script, _setup, sha256, waitFor, _program, _log, new Version(0, 0, 38));
        using var process = Process.Start(info)!;
        Assert.True(process.WaitForExit(60_000), "o script não terminou");
        return process.ExitCode;
    }

    // The programs the script starts aren't waited for: what they write down
    // may land a moment later.
    private static string WhenWritten(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } text)
                {
                    return text;
                }
            }
            catch (IOException)
            {
                // still being written
            }
            Thread.Sleep(50);
        }
        throw new TimeoutException($"{path} não foi escrito");
    }

    private static int GonePid()
    {
        using var gone = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        gone.WaitForExit();
        return gone.Id;
    }

    [Fact]
    public void ACheckedSetup_IsRunSilentlyAsTeacher_ThenTheMidviewOpensWithHowItEnded()
    {
        SetupThatEnds(0);

        Run(Sha256Of(_setup), GonePid());

        var setupArgs = WhenWritten(_setupCalls);
        Assert.Contains("/SILENT", setupArgs);
        Assert.Contains("/SUPPRESSMSGBOXES", setupArgs);
        Assert.Contains("/TIPO=professor", setupArgs);
        Assert.Contains($"/LOG=\"{_log}\"", setupArgs);
        Assert.Equal("--atualizacao-terminou 0 0.0.38", WhenWritten(_programCalls));
        Assert.False(File.Exists(_setup)); // done with: 100 MB less in the teacher's folder
    }

    [Fact]
    public void ASetupThatFails_StillOpensTheMidview_WithItsCode()
    {
        SetupThatEnds(5);

        Run(Sha256Of(_setup), GonePid());

        Assert.Equal("--atualizacao-terminou 5 0.0.38", WhenWritten(_programCalls));
    }

    // Changed between the download's check and now: never run.
    [Fact]
    public void ASetupThatChanged_IsNotRun()
    {
        SetupThatEnds(0);

        Run(new string('0', 64), GonePid());

        Assert.Equal("--atualizacao-terminou -1 0.0.38", WhenWritten(_programCalls));
        Assert.False(File.Exists(_setupCalls));
    }

    // The setup refuses to replace a Midview still open: the script waits for it.
    [Fact]
    public void TheSetup_WaitsForTheMidviewToClose()
    {
        SetupThatEnds(0);
        using var midview = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;

        Run(Sha256Of(_setup), midview.Id);
        WhenWritten(_setupCalls);

        Assert.True(midview.HasExited);
        Assert.True(File.GetLastWriteTimeUtc(_setupCalls) >= midview.ExitTime.ToUniversalTime());
    }
}
