using System.Diagnostics;
using Microsoft.Win32;

namespace Midview.Agent;

// Lists the programs installed on this machine and uninstalls them silently,
// as the Agent's SYSTEM identity, for the console's "Desinstalar programa".
// Reads the same place "Adicionar ou remover programas" does (the Uninstall
// keys under HKLM, 64- and 32-bit), and only ever runs an uninstall that can
// run silently -- a QuietUninstallString, or msiexec for an MSI. A program
// without a silent uninstall is listed but reported as not removable this
// way, never run (a normal uninstaller launched as SYSTEM would wait on a
// window nobody can see).
//
// The base key and the command runner are injected so tests use a throwaway
// HKCU subtree and a fake runner instead of the machine's real keys and
// processes.
public sealed class ProgramUninstaller
{
    private static readonly string[] UninstallPaths =
    {
        @"Microsoft\Windows\CurrentVersion\Uninstall",
        @"WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    private readonly Lazy<RegistryKey> _base;
    private readonly Func<string, int?> _run;

    public ProgramUninstaller(RegistryKey machineBase, Func<string, int?> runCommand)
        : this(() => machineBase, runCommand)
    {
    }

    private ProgramUninstaller(Func<RegistryKey> machineBaseFactory, Func<string, int?> runCommand)
    {
        _base = new Lazy<RegistryKey>(machineBaseFactory);
        _run = runCommand;
    }

    // HKLM\SOFTWARE (read lazily, so a failure here can't stop the Agent
    // starting) and the real command runner.
    public static ProgramUninstaller OpenMachine() =>
        new(() => Registry.LocalMachine.OpenSubKey("SOFTWARE", writable: false)
                  ?? throw new IOException("HKLM\\SOFTWARE não pôde ser aberta"),
            RunCommand);

    // The display names a teacher can choose from, each once, in order.
    public IReadOnlyList<string> ListInstalled()
    {
        var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in Entries())
        {
            names.Add(entry.DisplayName);
        }
        return names.ToList();
    }

    // Uninstalls every program whose display name is given, silently. Returns
    // a summary for the teacher's per-machine result.
    public (bool Succeeded, string Detail) UninstallMany(IReadOnlyList<string> displayNames)
    {
        int removed = 0, notFound = 0, noSilent = 0, failed = 0;
        foreach (var name in displayNames)
        {
            switch (UninstallOne(name))
            {
                case UninstallOutcome.Removed: removed++; break;
                case UninstallOutcome.NotFound: notFound++; break;
                case UninstallOutcome.NoSilentUninstall: noSilent++; break;
                default: failed++; break;
            }
        }

        var parts = new List<string>();
        if (removed > 0) parts.Add($"{removed} desinstalado(s)");
        if (notFound > 0) parts.Add($"{notFound} não instalado(s) aqui");
        if (noSilent > 0) parts.Add($"{noSilent} sem desinstalação silenciosa");
        if (failed > 0) parts.Add($"{failed} falhou(ram)");
        var detail = parts.Count == 0 ? "nada a desinstalar" : string.Join(", ", parts);
        return (failed == 0, detail);
    }

    private enum UninstallOutcome { Removed, NotFound, NoSilentUninstall, Failed }

    private UninstallOutcome UninstallOne(string displayName)
    {
        // The same program can sit under both the 64- and 32-bit hives (some
        // runtimes do); run every entry with that name, not just the first.
        var matches = Entries()
            .Where(e => string.Equals(e.DisplayName, displayName.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            return UninstallOutcome.NotFound;
        }

        var silent = matches.Where(e => e.SilentCommand is not null).ToList();
        if (silent.Count == 0)
        {
            AgentLog.Warn($"'{displayName}' has no silent uninstall; not removing it.");
            return UninstallOutcome.NoSilentUninstall;
        }

        var anyFailed = false;
        var anyRemoved = false;
        foreach (var entry in silent)
        {
            try
            {
                var code = _run(entry.SilentCommand!);
                // 0 = done; 3010 = done, needs a reboot; 1641 = done, reboot
                // started. Anything else (or no exit code) is a failure.
                if (code is 0 or 3010 or 1641)
                {
                    AgentLog.Warn($"Uninstalled '{displayName}' (exit {code}).");
                    anyRemoved = true;
                }
                else
                {
                    AgentLog.Warn($"Uninstall of '{displayName}' ended with {(code is null ? "no exit code" : code.ToString())}.");
                    anyFailed = true;
                }
            }
            catch (Exception ex)
            {
                AgentLog.Warn($"Uninstall of '{displayName}' failed: {ex.Message}");
                anyFailed = true;
            }
        }

        return anyFailed ? UninstallOutcome.Failed : anyRemoved ? UninstallOutcome.Removed : UninstallOutcome.NoSilentUninstall;
    }

    private sealed record Entry(string DisplayName, string? SilentCommand);

    private IEnumerable<Entry> Entries()
    {
        foreach (var path in UninstallPaths)
        {
            using var root = _base.Value.OpenSubKey(path, writable: false);
            if (root is null)
            {
                continue;
            }
            foreach (var subName in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(subName);
                if (key is null || ReadEntry(key, subName) is not { } entry)
                {
                    continue;
                }
                yield return entry;
            }
        }
    }

    private static Entry? ReadEntry(RegistryKey key, string subKeyName)
    {
        if (key.GetValue("DisplayName") is not string displayName || string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }
        // Windows' own components and updates aren't programs the teacher
        // removes; skip them.
        if (key.GetValue("SystemComponent") is int component && component == 1)
        {
            return null;
        }
        if (key.GetValue("ParentKeyName") is string parent && parent.Length > 0)
        {
            return null; // a patch/update under another program
        }

        return new Entry(displayName.Trim(), SilentCommandFor(key, subKeyName));
    }

    // The command that uninstalls without any window, or null when there
    // isn't one (so it's never run as SYSTEM in the background).
    private static string? SilentCommandFor(RegistryKey key, string subKeyName)
    {
        if (key.GetValue("QuietUninstallString") is string quiet && !string.IsNullOrWhiteSpace(quiet))
        {
            return quiet;
        }
        var isMsi = key.GetValue("WindowsInstaller") is int msi && msi == 1;
        if (isMsi && subKeyName.StartsWith('{') && subKeyName.EndsWith('}'))
        {
            return $"msiexec.exe /x {subKeyName} /qn /norestart";
        }
        return null;
    }

    // Runs a full command line through cmd.exe (so its own quoting holds),
    // with no window, and waits a few minutes. Returns the exit code, or null
    // if it couldn't start or didn't finish in time.
    private static int? RunCommand(string commandLine)
    {
        try
        {
            // "/s /c" plus one outer pair of quotes: cmd strips exactly that
            // pair and runs the rest verbatim, so an uninstall string with its
            // own quoted paths and arguments isn't mangled by cmd's quote rule.
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/s /c \"" + commandLine + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return null;
            }
            if (process.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds))
            {
                return process.ExitCode;
            }

            // It hung (a "silent" uninstaller that still wants a window nobody
            // can see in session 0). Don't leave it running as SYSTEM.
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Already gone, or couldn't be killed; nothing more to do.
            }
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }
}
