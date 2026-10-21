using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Midview.Agent;

// Whether a Desktop path is one the Agent, as SYSTEM, may protect and release
// folders on. The path comes from the student's own registry (HKCU User Shell
// Folders, which the student can write), so it could name another account's
// Desktop, or a folder reached through a link the student made: SYSTEM would
// then create "Arquivos do professor" and the class folder there, set their
// permissions and, at the end of a lesson, give the student full control of a
// folder by that name, whoever's it was. So the Desktop counts only where it
// really is (its final path, every link on the way resolved) and only if that
// is inside the student's own profile folder (also resolved), or the Desktop
// folder belongs to the student. ProtectedStudentFolders then checks the final
// path of every folder it opens against this one before it changes anything,
// so a link put in place after this check changes nothing either.
public static class StudentDesktopCheck
{
    private static readonly object LogGate = new();
    private static string? _lastRefusal;

    // The Desktop's final path when it may be used, or null and why not.
    // profilePathOf: the student's ProfileImagePath (ProfileImagePathOf, or a
    // test's own), null when not known: then only the owner can vouch for it.
    public static string? Verify(
        string desktop, SecurityIdentifier student, Func<SecurityIdentifier, string?> profilePathOf, out string refusal)
    {
        string desktopFinal;
        SecurityIdentifier? owner;
        try
        {
            using var handle = ProtectedStudentFolders.OpenAsItself(desktop, ProtectedStudentFolders.ReadControl);
            desktopFinal = FinalPath(handle);
            owner = ProtectedStudentFolders.OwnerOf(handle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            refusal = $"the Desktop {desktop} can't be used: {ex.Message}";
            return null;
        }

        if (owner is not null && student.Equals(owner))
        {
            refusal = string.Empty;
            return desktopFinal;
        }

        if (ProfileFinalPath(student, profilePathOf) is { } profile && IsUnder(desktopFinal, profile))
        {
            refusal = string.Empty;
            return desktopFinal;
        }

        refusal = $"the Desktop {desktop} is {desktopFinal}, outside {student.Value}'s profile and not theirs";
        return null;
    }

    // The student's ProfileImagePath (HKLM ProfileList), environment variables
    // expanded, or null. An administrator's key, not the student's.
    public static string? ProfileImagePathOf(SecurityIdentifier student)
    {
        try
        {
            using var profile = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{student.Value}");
            return profile?.GetValue("ProfileImagePath") is string path && path.Length > 0
                ? Environment.ExpandEnvironmentVariables(path)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    // A refusal goes to the Agent's log once, not at every poll for as long as it lasts.
    public static void ReportOnce(string refusal)
    {
        lock (LogGate)
        {
            if (refusal == _lastRefusal)
            {
                return;
            }
            _lastRefusal = refusal;
        }
        AgentLog.Warn($"The student's folders aren't protected: {refusal}.");
    }

    // Where an open file or folder really is: its normalized path, every link
    // on the way resolved, without the "\\?\" Windows puts in front.
    internal static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(512);
        while (true)
        {
            var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0)
            {
                throw new IOException($"o caminho real não pôde ser lido (erro {Marshal.GetLastWin32Error()})");
            }
            if (length < buffer.Capacity)
            {
                break;
            }
            buffer.EnsureCapacity((int)length + 1);
        }

        var path = buffer.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    // The same place, as paths go (ignoring case and a final separator).
    internal static bool SamePath(string a, string b) =>
        string.Equals(Trimmed(a), Trimmed(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string folder) =>
        Trimmed(path).StartsWith(Trimmed(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Trimmed(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string? ProfileFinalPath(SecurityIdentifier student, Func<SecurityIdentifier, string?> profilePathOf)
    {
        var profile = profilePathOf(student);
        if (string.IsNullOrWhiteSpace(profile))
        {
            return null;
        }

        try
        {
            using var handle = ProtectedStudentFolders.OpenAsItself(profile, ProtectedStudentFolders.ReadControl);
            return FinalPath(handle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
}
