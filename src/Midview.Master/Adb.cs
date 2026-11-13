using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Midview.Master;

// What one run of the phone-cable tool printed (stdout then stderr, as one text).
public sealed record AdbOutput(int ExitCode, string Text, bool TimedOut);

// The phone-cable tool, seen from PhoneCable: one call, with a time limit.
public interface IAdb
{
    AdbOutput Run(TimeSpan timeout, params string[] args);
}

// Runs Google's adb.exe (found or downloaded by AdbTool), on its own server
// port so it never fights another adb server on the PC. Never shows a
// console window.
public sealed class AdbProcess : IAdb, IDisposable
{
    public const string ServerPort = "5039";

    private readonly string _adbPath;
    private bool _used;

    public AdbProcess(string adbPath)
    {
        _adbPath = adbPath;
    }

    public AdbOutput Run(TimeSpan timeout, params string[] args)
    {
        _used = true;
        var start = new ProcessStartInfo(_adbPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        start.Environment["ANDROID_ADB_SERVER_PORT"] = ServerPort;

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("adb não iniciou");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new AdbOutput(-1, e.Message, false);
        }

        using (process)
        {
            // Both pipes are read at once, so a full buffer on one can't stall the other.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            var timedOut = !process.WaitForExit(timeout);
            if (timedOut)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception)
                {
                }
                process.WaitForExit(2000);
            }

            // A server adb started for this call can keep the pipes open after the
            // client is gone, so the reads get a short grace period, not a wait forever.
            Task.WaitAll(new Task[] { stdout, stderr }, TimeSpan.FromSeconds(2));
            var text = (stdout.IsCompletedSuccessfully ? stdout.Result : "")
                + (stderr.IsCompletedSuccessfully ? stderr.Result : "");
            var exit = timedOut || !process.HasExited ? -1 : process.ExitCode;
            return new AdbOutput(exit, text, timedOut);
        }
    }

    public void Dispose()
    {
        if (_used)
        {
            Run(TimeSpan.FromSeconds(10), "kill-server");
        }
    }

    // On the Master's exit, synchronously: an adb server (or call) left
    // running from the folder it was kept in, or from the program's adb
    // folder an older Midview installed, would hold adb.exe and its DLLs
    // (and fail the next update of the teacher's program). The server is
    // asked to stop, then whatever still runs from that folder is killed.
    // Only adb.exe from those folders is touched, never another program's adb.
    public static void StopLeftovers()
    {
        StopLeftovers(AdbTool.DefaultFolder, TimeSpan.FromSeconds(5));
        StopLeftovers(AdbTool.ProgramFolder, TimeSpan.FromSeconds(5));
    }

    internal static void StopLeftovers(string folder, TimeSpan killServerTimeout)
    {
        if (!AnyRunningFrom(folder))
        {
            return;
        }
        new AdbProcess(Path.Combine(folder, "adb.exe")).Run(killServerTimeout, "kill-server");
        foreach (var process in Process.GetProcessesByName("adb"))
        {
            using (process)
            {
                try
                {
                    if (IsUnder(process.MainModule?.FileName, folder))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(2000);
                    }
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Gone meanwhile, or not ours to look at.
                }
            }
        }
    }

    private static bool AnyRunningFrom(string folder)
    {
        var found = false;
        foreach (var process in Process.GetProcessesByName("adb"))
        {
            using (process)
            {
                try
                {
                    found |= IsUnder(process.MainModule?.FileName, folder);
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                }
            }
        }
        return found;
    }

    // Whether a program's full path is inside folder (not merely beside it:
    // "adb2\adb.exe" is not in "adb").
    internal static bool IsUnder(string? path, string folder)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder))
        {
            return false;
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
