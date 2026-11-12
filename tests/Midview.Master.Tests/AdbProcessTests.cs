using System;
using System.IO;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

public sealed class AdbProcessTests
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public void Shipped_adb_runs_without_a_phone()
    {
        // "version" needs no device and starts no server.
        using var adb = new AdbProcess(Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe"));
        var output = adb.Run(TimeSpan.FromSeconds(20), "version");
        Assert.Equal(0, output.ExitCode);
        Assert.False(output.TimedOut);
        Assert.Contains("Android Debug Bridge", output.Text);
    }

    [Fact]
    public void Reads_stdout_and_stderr_fully()
    {
        using var adb = new AdbProcess(Cmd);
        var output = adb.Run(TimeSpan.FromSeconds(20), "/c", "echo out& echo err 1>&2");
        Assert.Contains("out", output.Text);
        Assert.Contains("err", output.Text);
    }

    [Fact]
    public void Big_output_does_not_deadlock()
    {
        using var adb = new AdbProcess(Cmd);
        var output = adb.Run(TimeSpan.FromSeconds(30), "/c", "for /l %i in (1,1,20000) do @echo line %i of padding text padding text");
        Assert.False(output.TimedOut);
        Assert.Contains("line 20000 of", output.Text);
    }

    [Fact]
    public void Slow_command_is_killed_on_timeout()
    {
        using var adb = new AdbProcess(Cmd);
        var output = adb.Run(TimeSpan.FromMilliseconds(500), "/c", "ping -n 30 127.0.0.1 >nul");
        Assert.True(output.TimedOut);
    }

    [Fact]
    public void Missing_program_is_reported_not_thrown()
    {
        using var adb = new AdbProcess(@"C:\nao-existe\adb.exe");
        var output = adb.Run(TimeSpan.FromSeconds(5), "devices");
        Assert.Equal(-1, output.ExitCode);
        Assert.False(output.TimedOut);
    }
}
