using System;
using System.IO;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

public sealed class AdbProcessTests
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

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

    [Theory]
    [InlineData(@"C:\Program Files\Midview Professor\adb\adb.exe", @"C:\Program Files\Midview Professor\adb", true)]
    [InlineData(@"c:\program files\midview professor\ADB\adb.exe", @"C:\Program Files\Midview Professor\adb\", true)]
    [InlineData(@"C:\Program Files\Midview Professor\adb2\adb.exe", @"C:\Program Files\Midview Professor\adb", false)]
    [InlineData(@"C:\Android\platform-tools\adb.exe", @"C:\Program Files\Midview Professor\adb", false)]
    [InlineData(null, @"C:\Program Files\Midview Professor\adb", false)]
    [InlineData("", @"C:\Program Files\Midview Professor\adb", false)]
    public void Only_programs_inside_the_adb_folder_are_ours(string? path, string folder, bool ours)
    {
        Assert.Equal(ours, AdbProcess.IsUnder(path, folder));
    }

    [Fact]
    public void Leftovers_from_the_adb_folder_are_stopped_and_others_are_not()
    {
        // A copy of ping named adb.exe stands in for a server left running:
        // the real adb is never started here.
        var root = Path.Combine(Path.GetTempPath(), "midview-adb-" + Guid.NewGuid().ToString("N"));
        var ours = Path.Combine(root, "adb");
        var other = Path.Combine(root, "adb2");
        Directory.CreateDirectory(ours);
        Directory.CreateDirectory(other);
        var ping = Path.Combine(Environment.SystemDirectory, "PING.EXE");
        File.Copy(ping, Path.Combine(ours, "adb.exe"));
        File.Copy(ping, Path.Combine(other, "adb.exe"));
        using var left = StartHidden(Path.Combine(ours, "adb.exe"));
        using var notOurs = StartHidden(Path.Combine(other, "adb.exe"));
        try
        {
            AdbProcess.StopLeftovers(ours, TimeSpan.FromSeconds(3));

            Assert.True(left.WaitForExit(5000));
            Assert.False(notOurs.HasExited);
        }
        finally
        {
            foreach (var process in new[] { left, notOurs })
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Nothing_left_running_means_nothing_is_run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "midview-adb-" + Guid.NewGuid().ToString("N"));
        // No adb.exe there at all: a call would only fail, and none is made.
        AdbProcess.StopLeftovers(folder, TimeSpan.FromSeconds(1));
        Assert.False(Directory.Exists(folder));
    }

    private static System.Diagnostics.Process StartHidden(string program)
    {
        var start = new System.Diagnostics.ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("-n");
        start.ArgumentList.Add("60");
        start.ArgumentList.Add("127.0.0.1");
        return System.Diagnostics.Process.Start(start)!;
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
