using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace Midview.Agent;

// Lists the programs installed on this machine and uninstalls them silently,
// for the console's "Desinstalar programa". Reads the same places "Adicionar
// ou remover programas" does: the Uninstall keys under HKLM (64- and 32-bit,
// every student's programs) and, when someone's logged on, that student's own
// HKCU (programs installed "just for me", which HKLM alone misses entirely).
// A machine-wide program is removed as the Agent's SYSTEM identity; a per-user
// one is removed in the student's own session instead, because its uninstaller
// only removes the copy belonging to whoever runs it -- run as SYSTEM it would
// leave the student's copy behind (OneDrive is the usual one). Only ever runs a
// command believed to be silent: a QuietUninstallString, msiexec for an MSI,
// or -- when neither is published -- the program's own UninstallString with the
// switches the two big non-MSI installer engines (NSIS, Inno Setup) each
// recognize and harmlessly ignore from the other, so one guess covers most
// EXE installers without knowing which engine built them. Office Click-to-Run
// is the one named exception: its published UninstallString is an interactive
// Add/Remove Programs call, so DisplayLevel=False is added to make it silent
// (see SilentArguments). A program whose UninstallString can't even be resolved
// to a real file is listed but reported as not removable this way, never run
// (a normal uninstaller launched as SYSTEM would wait on a window nobody can
// see).
//
// The base keys and the command runner are injected so tests use throwaway
// subtrees and a fake runner instead of the machine's real keys and
// processes.
public sealed class ProgramUninstaller
{
    private static readonly string[] UninstallPaths =
    {
        @"Microsoft\Windows\CurrentVersion\Uninstall",
        @"WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    private readonly Lazy<RegistryKey> _base;
    private readonly Func<RegistryKey?> _userBase;
    private readonly Func<string, int?> _run;
    private readonly Func<string, int?> _runInStudentSession;
    private readonly Func<IReadOnlyList<string>> _listAppxPackages;

    // userBase: the logged-on student's own SOFTWARE hive, re-resolved on
    // every read (unlike machineBase, who's logged on can change between one
    // class and the next) -- null skips HKCU entries for that read. Defaults
    // to always null, so existing callers that only care about the machine
    // hive don't need to know this exists. listAppxPackages: Store/UWP
    // packages as "Name|PackageFullName" lines (AppxEntries); defaults to
    // none, for the same reason.
    // runInStudentSession: runs a command as the student logged on at the
    // console rather than as this SYSTEM service -- used for their "just for me"
    // programs, whose uninstaller only removes the copy belonging to whoever
    // runs it. Defaults to the same runner as everything else, so a caller that
    // doesn't distinguish the two (and the tests that don't) keeps today's
    // behavior.
    public ProgramUninstaller(
        RegistryKey machineBase, Func<string, int?> runCommand,
        Func<RegistryKey?>? userBase = null, Func<IReadOnlyList<string>>? listAppxPackages = null,
        Func<string, int?>? runInStudentSession = null)
        : this(() => machineBase, userBase ?? (() => null), runCommand,
               listAppxPackages ?? (() => Array.Empty<string>()), runInStudentSession ?? runCommand)
    {
    }

    private ProgramUninstaller(
        Func<RegistryKey> machineBaseFactory, Func<RegistryKey?> userBase,
        Func<string, int?> runCommand, Func<IReadOnlyList<string>> listAppxPackages,
        Func<string, int?> runInStudentSession)
    {
        _base = new Lazy<RegistryKey>(machineBaseFactory);
        _userBase = userBase;
        _run = runCommand;
        _runInStudentSession = runInStudentSession;
        _listAppxPackages = listAppxPackages;
    }

    // HKLM\SOFTWARE (read lazily, so a failure here can't stop the Agent
    // starting), the logged-on student's own SOFTWARE hive, the real command
    // runner, and the real AppX package lister.
    public static ProgramUninstaller OpenMachine() =>
        new(() => Registry.LocalMachine.OpenSubKey("SOFTWARE", writable: false)
                  ?? throw new IOException("HKLM\\SOFTWARE não pôde ser aberta"),
            UserSoftwareBase,
            RunCommand,
            ListInstalledAppxPackages,
            UserSessionProcessLauncher.RunInLoggedOnStudentSession);

    // The console's logged-on student's own SOFTWARE hive (HKEY_USERS\<their
    // SID>\SOFTWARE) -- a program they installed "just for me" lives here,
    // under HKCU as far as they're concerned, not under the Agent's own
    // SYSTEM profile that Registry.CurrentUser would actually open from a
    // service. Only readable while their profile is loaded, which Windows
    // does exactly when they're logged in -- null between classes, at the
    // lock screen, or if nobody's at the console, and HKCU entries are simply
    // skipped for that read rather than treated as an error.
    private static RegistryKey? UserSoftwareBase()
    {
        if (UserSessionProcessLauncher.LoggedOnStudent() is not { } student)
        {
            return null;
        }
        try
        {
            return Registry.Users.OpenSubKey($@"{student.Student.Value}\SOFTWARE", writable: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AgentLog.Warn($"Couldn't open the logged-on student's SOFTWARE hive: {ex.Message}");
            return null;
        }
    }

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
        // Not installed here is fine -- there was nothing for this machine to
        // do. Not removable (noSilent) is not: the teacher asked to remove
        // something that's actually on this machine and it's still there, so
        // that must not read as success alongside a real failure.
        return (noSilent == 0 && failed == 0, detail);
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

        // An entry that removes a whole suite (Office 2007's Setup Controller)
        // stands in for its same-named siblings -- run it alone, or the sibling
        // MSI would run too and fail once the suite is already gone.
        if (silent.Any(e => e.SupersedesSameName))
        {
            silent = silent.Where(e => e.SupersedesSameName).ToList();
        }

        var anyFailed = false;
        var anyRemoved = false;
        foreach (var entry in silent)
        {
            // A program from the student's own hive is removed in their session;
            // everything machine-wide, as this SYSTEM service.
            var run = entry.PerUser ? _runInStudentSession : _run;
            try
            {
                var code = run(entry.SilentCommand!);
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

    private sealed record Entry(string DisplayName, string? SilentCommand, bool PerUser = false, bool SupersedesSameName = false);

    private IEnumerable<Entry> Entries()
    {
        foreach (var entry in RegistryEntries(_base.Value, perUser: false))
        {
            yield return entry;
        }

        // The logged-on student's own SOFTWARE hive, when there is one --
        // re-resolved here, not cached, so a different student logged on since
        // the last read is picked up rather than a stale hive handle from
        // whoever was at the console before. Its programs are "just for me": the
        // uninstaller has to run as that student (PerUser), not as this SYSTEM
        // service, or it would leave the student's copy behind.
        if (_userBase() is { } userSoftware)
        {
            using (userSoftware)
            {
                foreach (var entry in RegistryEntries(userSoftware, perUser: true))
                {
                    yield return entry;
                }
            }
        }

        foreach (var entry in AppxEntries())
        {
            yield return entry;
        }
    }

    private static IEnumerable<Entry> RegistryEntries(RegistryKey softwareRoot, bool perUser)
    {
        foreach (var path in UninstallPaths)
        {
            using var root = softwareRoot.OpenSubKey(path, writable: false);
            if (root is null)
            {
                continue;
            }
            foreach (var subName in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(subName);
                if (key is null || ReadEntry(key, subName, perUser) is not { } entry)
                {
                    continue;
                }
                yield return entry;
            }
        }
    }

    // Store/UWP apps (Copilot among them, depending on how it got onto a
    // machine) don't publish a classic UninstallString at all -- Windows
    // removes them through its package manager instead. Kept behind the same
    // Entry/SilentCommand shape as everything else above, so the rest of this
    // class (matching by name, running the command, reporting the result)
    // doesn't need to know the difference. _listAppxPackages already excludes
    // shared framework packages (removing one could break every app that
    // depends on it), resource-only packages (not a program on their own),
    // and packages Windows itself marks NonRemovable (it would just refuse,
    // the same as the missing Uninstall button for it in Settings).
    private IEnumerable<Entry> AppxEntries()
    {
        foreach (var line in _listAppxPackages())
        {
            var separator = line.IndexOf('|');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue; // malformed line (shouldn't happen); skip rather than guess
            }
            var name = line[..separator];
            var packageFullName = line[(separator + 1)..];
            yield return new Entry(name, AppxRemovalCommand(packageFullName));
        }
    }

    private static Entry? ReadEntry(RegistryKey key, string subKeyName, bool perUser)
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

        var (command, supersedesSameName) = SilentCommandFor(key, subKeyName);
        return new Entry(displayName.Trim(), command, perUser, supersedesSameName);
    }

    // The command that uninstalls without any window, or null when there
    // isn't one to even try (so it's never run as SYSTEM in the background).
    // SupersedesSameName is true for an entry that removes a whole suite other
    // same-named entries belong to (Office 2007's Setup Controller), so those
    // siblings aren't also run.
    private static (string? Command, bool SupersedesSameName) SilentCommandFor(RegistryKey key, string subKeyName)
    {
        if (key.GetValue("QuietUninstallString") is string quiet && !string.IsNullOrWhiteSpace(quiet))
        {
            return (quiet, false);
        }
        var isMsi = key.GetValue("WindowsInstaller") is int msi && msi == 1;
        if (isMsi && subKeyName.StartsWith('{') && subKeyName.EndsWith('}'))
        {
            return ($"msiexec.exe /x {subKeyName} /qn /norestart", false);
        }

        // No QuietUninstallString and not an MSI: fall back to the plain
        // UninstallString. When it already carries arguments, they're kept
        // exactly as published and nothing is added -- for some installers
        // (notably Microsoft's own "setup.exe --uninstall --channel=... "
        // pattern) those arguments ARE the uninstall invocation, not
        // stylistic noise, and overriding them with a generic switch could
        // silently do the wrong thing (e.g. reinstall) instead of nothing.
        // Only a bare executable path with no arguments gets the two generic
        // installer engines' own silent switches appended: /S is NSIS's
        // (Inno Setup ignores an unknown switch); /VERYSILENT
        // /SUPPRESSMSGBOXES /NORESTART are Inno Setup's (NSIS ignores them
        // the same way), so sending both covers either without knowing which
        // one built this program.
        if (key.GetValue("UninstallString") is string raw && !string.IsNullOrWhiteSpace(raw))
        {
            var (exePath, arguments) = SplitCommand(raw);
            if (exePath is not null)
            {
                if (string.IsNullOrEmpty(arguments))
                {
                    return ($"\"{exePath}\" {GenericSilentSwitches(exePath)}", false);
                }
                if (Office2007ProductToUninstall(exePath, arguments) is { } product)
                {
                    return (Office2007SilentUninstall(exePath, product), true);
                }
                return ($"\"{exePath}\" {SilentArguments(exePath, arguments)}", false);
            }
        }

        return (null, false);
    }

    // Office 2007's ARP entry runs "...\Office Setup Controller\setup.exe"
    // /uninstall <PRODUCT> /dll OSETUP.DLL -- a visible uninstall (no good in
    // session 0) with no DisplayLevel switch of its own. Returns the <PRODUCT>
    // to remove, or null when this isn't that pattern.
    private static string? Office2007ProductToUninstall(string exePath, string arguments)
    {
        if (!string.Equals(Path.GetFileName(exePath), "setup.exe", StringComparison.OrdinalIgnoreCase)
            || arguments.IndexOf("OSETUP.DLL", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }
        var tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var uninstall = Array.FindIndex(tokens, t => string.Equals(t, "/uninstall", StringComparison.OrdinalIgnoreCase));
        return uninstall >= 0 && uninstall + 1 < tokens.Length ? tokens[uninstall + 1] : null;
    }

    // Removes an Office 2007 product silently. Its setup.exe only goes quiet
    // with a config.xml that sets Display Level="none", so the command writes
    // one to a temp file, runs setup.exe /uninstall <PRODUCT> /config <file>,
    // and deletes it after -- all as a PowerShell -EncodedCommand, the same way
    // AppxRemovalCommand carries a multi-statement script through cmd.exe
    // intact. Removing the suite also removes its same-named MSI entry, so that
    // sibling isn't run too (see SupersedesSameName).
    private static string Office2007SilentUninstall(string setupExePath, string product)
    {
        var escapedPath = setupExePath.Replace("'", "''");
        var config =
            $"<Configuration Product=\"{product}\">" +
            "<Display Level=\"none\" CompletionNotice=\"no\" SuppressModal=\"yes\" AcceptEula=\"yes\" />" +
            "<Setting Id=\"SETUP_REBOOT\" Value=\"Never\" />" +
            "</Configuration>";
        var script =
            "$ErrorActionPreference = 'Stop'; " +
            $"$xml = '{config.Replace("'", "''")}'; " +
            "$f = Join-Path $env:TEMP ('midview-office2007-' + [guid]::NewGuid().ToString('N') + '.xml'); " +
            "Set-Content -LiteralPath $f -Value $xml -Encoding UTF8; " +
            "try { " +
            $"& '{escapedPath}' /uninstall {product} /config $f; " +
            "$code = $LASTEXITCODE " +
            "} finally { Remove-Item -LiteralPath $f -ErrorAction SilentlyContinue }; " +
            "if ($null -eq $code) { exit 1 } else { exit $code }";
        return $"powershell.exe {PowerShellArguments(script)}";
    }

    // The published arguments, kept as they are for every installer but one.
    // Office Click-to-Run publishes its uninstall as an "Add/Remove Programs"
    // (scenariosubtype=ARP) call of OfficeClickToRun.exe with no DisplayLevel,
    // so it tries to put a progress window on screen. Run as SYSTEM in session 0
    // there's no screen for it and the removal never finishes, so the Office is
    // listed, "uninstalled", and still there. DisplayLevel=False hands the work
    // to the Click-to-Run service (itself SYSTEM) with no UI -- the supported
    // silent removal; forceappshutdown=True closes an Office app still holding a
    // file open, which nobody could close by hand in session 0 either. Both are
    // added only when the string doesn't already set them, so a string that's
    // already silent is left to say so once.
    private static string SilentArguments(string exePath, string arguments)
    {
        var isOfficeClickToRunArp =
            string.Equals(Path.GetFileName(exePath), "OfficeClickToRun.exe", StringComparison.OrdinalIgnoreCase)
            && arguments.Contains("scenariosubtype=ARP", StringComparison.OrdinalIgnoreCase);
        if (!isOfficeClickToRunArp)
        {
            return arguments;
        }

        if (!arguments.Contains("DisplayLevel=", StringComparison.OrdinalIgnoreCase))
        {
            arguments += " DisplayLevel=False";
        }
        if (!arguments.Contains("forceappshutdown=", StringComparison.OrdinalIgnoreCase))
        {
            arguments += " forceappshutdown=True";
        }
        return arguments;
    }

    // The silent switches to append to a bare UninstallString (one with no
    // arguments of its own). /S is NSIS's and Inno Setup ignores it; /VERYSILENT
    // /SUPPRESSMSGBOXES /NORESTART are Inno Setup's and NSIS ignores them -- so
    // both cover either engine. WinRAR's own uninstaller is the exception: it
    // goes silent with /S alone and exits 0 doing nothing when the Inno switches
    // are tacked on too, so it gets just /S.
    private static string GenericSilentSwitches(string exePath)
    {
        var directory = Path.GetDirectoryName(exePath);
        var isWinRarUninstaller =
            string.Equals(Path.GetFileName(exePath), "uninstall.exe", StringComparison.OrdinalIgnoreCase)
            && directory is not null
            && string.Equals(Path.GetFileName(directory.TrimEnd('\\', '/')), "WinRAR", StringComparison.OrdinalIgnoreCase);
        return isWinRarUninstaller ? "/S" : "/S /VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
    }

    // Splits a raw UninstallString into its executable path and whatever
    // arguments already follow it, so a generic silent switch is appended
    // only when there's nothing there to lose. Handles a quoted path
    // ("C:\...\un.exe" /args) directly, and Windows' own ambiguous
    // unquoted-with-spaces form (C:\Program Files\...\un.exe, with or without
    // trailing arguments) by checking the real file -- this runs on the
    // machine that has it, so the check is meaningful. A null path means no
    // unquoted candidate matched a real file, so a malformed entry is never
    // run.
    private static (string? ExePath, string Arguments) SplitCommand(string uninstallString)
    {
        var text = uninstallString.Trim();
        if (text.StartsWith('"'))
        {
            var closing = text.IndexOf('"', 1);
            return closing > 0 ? (text[1..closing], text[(closing + 1)..].Trim()) : (null, "");
        }

        if (File.Exists(text))
        {
            return (text, "");
        }

        // Unquoted with trailing arguments: try each space as a possible
        // split, shortest first -- the same resolution Windows itself uses
        // for this ambiguous form.
        var spaceIndex = text.IndexOf(' ');
        while (spaceIndex > 0)
        {
            var candidate = text[..spaceIndex];
            if (File.Exists(candidate))
            {
                return (candidate, text[(spaceIndex + 1)..].Trim());
            }
            spaceIndex = text.IndexOf(' ', spaceIndex + 1);
        }
        return (null, "");
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

    // Every removable Store/UWP package as "Name|PackageFullName" lines.
    // Name isn't always the polished title Settings shows -- e.g.
    // "Microsoft.549981C3F5F10" for Cortana -- resolving the real,
    // sometimes-localized display string needs an indirect-resource lookup
    // this keeps out of scope; Name is stable, unambiguous, and good enough
    // for a teacher to recognize most of the time. 30 s ceiling: enumerating
    // every package is normally well under that, but this must never hang
    // the Agent's answer to the teacher.
    private static IReadOnlyList<string> ListInstalledAppxPackages()
    {
        const string script =
            "Get-AppxPackage -AllUsers | " +
            "Where-Object { -not $_.IsFramework -and -not $_.IsResourcePackage -and -not $_.NonRemovable } | " +
            "ForEach-Object { \"$($_.Name)|$($_.PackageFullName)\" }";

        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", PowerShellArguments(script))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (process is null)
            {
                return Array.Empty<string>();
            }

            var lines = new List<string>();
            string? line;
            while ((line = process.StandardOutput.ReadLine()) is not null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line.Trim());
                }
            }

            if (!process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
            return lines;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            AgentLog.Warn($"Couldn't list AppX packages: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    // The command that removes one Store/UWP package: for the accounts
    // currently on the machine (-AllUsers, same as the registry-based path
    // reaching every student, not just whoever's logged on right now) and,
    // when it was provisioned for new accounts too, so a different student
    // number logging in later doesn't get it reinstalled automatically.
    // Explicit exit codes, since a non-terminating PowerShell error wouldn't
    // otherwise turn into a non-zero one on its own.
    private static string AppxRemovalCommand(string packageFullName)
    {
        var escaped = packageFullName.Replace("'", "''");
        var script =
            "$ErrorActionPreference = 'Stop'; " +
            "try { " +
            $"Remove-AppxPackage -Package '{escaped}' -AllUsers; " +
            $"Get-AppxProvisionedPackage -Online | Where-Object {{ $_.PackageName -eq '{escaped}' }} | Remove-AppxProvisionedPackage -Online -NoRestart | Out-Null; " +
            "exit 0 " +
            "} catch { exit 1 }";
        return $"powershell.exe {PowerShellArguments(script)}";
    }

    // -EncodedCommand (Base64 UTF-16LE) sidesteps the escaping that quoting a
    // multi-statement script through cmd.exe's own quote-stripping (see
    // RunCommand) would otherwise need.
    private static string PowerShellArguments(string script) =>
        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
