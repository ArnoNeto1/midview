using Midview.Protocol;

namespace Midview.Agent;

// Who owns a program's image file: installed by an administrator (or
// Windows itself), created by the logged-on student, or unknown.
public enum FileOwnerKind
{
    Unknown,
    Installed,
    Student,
}

// What the Agent knows about one running process, gathered by
// WindowsProcessInspector. Names are normalized exe names (see
// AppBlockPolicy.NormalizeExeName); OriginalFilename is null when the file
// has no version information.
public sealed record ProcessFacts(
    int ProcessId,
    DateTime StartTime,
    int SessionId,
    bool OwnedBySessionUser,
    bool IsElevated,
    string ImageName,
    string? OriginalFilename,
    bool InWindowsFolder,
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

    // A protected name (Midview's included) loses its protection only when
    // the file is known to be the student's own: a program renamed to look
    // like part of Windows. When in doubt, it stays protected.
    public static bool IsProtected(ProcessFacts process) =>
        process.FileOwner != FileOwnerKind.Student && ProtectedPrograms.IsProtected(process.ImageName);

    // In the Windows folder and not the student's own file (some folders
    // under it are writable by standard users), or an installed Windows
    // helper.
    public static bool IsWindowsItself(ProcessFacts process) =>
        process.FileOwner != FileOwnerKind.Student
        && (process.InWindowsFolder || WindowsHelpers.Contains(process.ImageName));

    // By name; a file the student brought in must also be that program by
    // its version information.
    private static bool IsAllowed(HashSet<string> listed, ProcessFacts process) =>
        listed.Contains(process.ImageName)
        && (process.FileOwner != FileOwnerKind.Student
            || (process.OriginalFilename is { } original && listed.Contains(original)));
}
