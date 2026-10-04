using Midview.Protocol;

namespace Midview.Agent;

// Who owns a program's image file, as far as program blocking can tell:
// installed by Windows or an administrator (owner SYSTEM, Administrators or
// TrustedInstaller), created by the logged-on student, or anything else --
// another user, or an owner that couldn't be read (FAT/exFAT volumes have
// none). Only a positively known trusted owner earns any protection.
public enum FileOwnerKind
{
    Other,
    Trusted,
    Student,
}

// What the Agent knows about one running process, gathered by
// WindowsProcessInspector. Names are normalized exe names (see
// AppBlockPolicy.NormalizeExeName); OriginalFilename is null when the file
// has no version information. InWindowsFolder: the image is under the
// Windows folder; InTrustedFolder: under the Windows folder, Program Files
// (either) or the Agent's own folder -- places a standard user can't write
// (except a few folders under Windows that the owner check covers).
public sealed record ProcessFacts(
    int ProcessId,
    DateTime StartTime,
    int SessionId,
    bool OwnedBySessionUser,
    bool IsElevated,
    string ImageName,
    string? OriginalFilename,
    bool InWindowsFolder,
    bool InTrustedFolder,
    FileOwnerKind FileOwner,
    string DisplayName);

// Decides which processes program blocking ends (docs/superpowers/specs/
// 2026-09-27-app-blocking-design.md). Kept apart from the Win32 calls so the
// rules can be tested anywhere.
public static class AppBlockRules
{
    // Windows runtime helpers outside the Windows folder that Windows and
    // Office use; allowed in allow-only mode when installed.
    private static readonly HashSet<string> WindowsHelpers = new(StringComparer.Ordinal)
    {
        "msedgewebview2.exe",
    };

    // Programs that run whatever the student types or drops on them. They
    // live in the Windows folder, but "allow only" is meant to keep a student
    // inside the chosen programs: here they need to be allowed like any other.
    private static readonly HashSet<string> ScriptHosts = new(StringComparer.Ordinal)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "powershell_ise.exe", "wscript.exe", "cscript.exe",
        "mshta.exe", "wsl.exe", "bash.exe",
    };

    public static bool ShouldEnd(AppBlockPolicy policy, ProcessFacts process)
    {
        if (!policy.IsActive || !IsCandidate(process))
        {
            return false;
        }

        var listed = policy.Programs.ToHashSet(StringComparer.Ordinal);
        return policy.Mode switch
        {
            AppBlockMode.BlockList =>
                listed.Contains(process.ImageName)
                || (process.OriginalFilename is { } original && listed.Contains(original)),
            AppBlockMode.AllowOnly => !IsWindowsItself(process) && !IsAllowed(listed, process),
            _ => false,
        };
    }

    // The fence: only the logged-on user's own, non-elevated processes in an
    // interactive session, and never the protected programs or Midview.
    public static bool IsCandidate(ProcessFacts process) =>
        process.SessionId != 0
        && process.OwnedBySessionUser
        && !process.IsElevated
        && !IsProtected(process);

    // A protected name (Midview's included) is protected only where it is
    // known to be the real thing: in a place a student can't write, with a
    // trusted owner. A file that merely carries the name -- on a USB stick,
    // in Downloads, on a share, or one whose owner couldn't be read -- gets
    // no protection.
    public static bool IsProtected(ProcessFacts process) =>
        ProtectedPrograms.IsProtected(process.ImageName)
        && process.InTrustedFolder
        && process.FileOwner == FileOwnerKind.Trusted;

    // In the Windows folder with a trusted owner (some folders under it are
    // writable by standard users, and what a student puts there is theirs),
    // and not a script host; or an installed Windows helper.
    public static bool IsWindowsItself(ProcessFacts process) =>
        process.FileOwner == FileOwnerKind.Trusted
        && ((process.InWindowsFolder && !ScriptHosts.Contains(process.ImageName))
            || (process.InTrustedFolder && WindowsHelpers.Contains(process.ImageName)));

    // By name; unless an administrator installed the file, it must also be
    // that program by its version information.
    private static bool IsAllowed(HashSet<string> listed, ProcessFacts process) =>
        listed.Contains(process.ImageName)
        && (process.FileOwner == FileOwnerKind.Trusted
            || (process.OriginalFilename is { } original && listed.Contains(original)));
}
