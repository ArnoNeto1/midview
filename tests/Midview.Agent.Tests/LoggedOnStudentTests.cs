using System.Security.Principal;
using Microsoft.Win32;
using Midview.Agent;
using Xunit;

// The student at the console when their Desktop is refused
// (StudentDesktopCheck): still the student, for everything that isn't Desktop
// work, such as the programs they installed "just for me" and the removals
// the teacher left pending for them. A made-up "profile" under %TEMP%, and a
// throwaway HKCU subtree for their registry: never the real ones.
public sealed class LoggedOnStudentTests : IDisposable
{
    private static readonly SecurityIdentifier Student = new("S-1-5-21-1111111111-2222222222-3333333333-1001");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-loggedon-{Guid.NewGuid():N}");
    private readonly string _profile;
    private readonly string _outside;
    private readonly string _pendingPath = Path.Combine(Path.GetTempPath(), $"midview-pending-{Guid.NewGuid():N}.json");
    private readonly string _registryPath = $@"Software\MidviewTest\{Guid.NewGuid():N}";

    public LoggedOnStudentTests()
    {
        _profile = Path.Combine(_root, "perfil");
        _outside = Path.Combine(_root, "Windows");
        Directory.CreateDirectory(Path.Combine(_profile, "Desktop"));
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        foreach (var path in new[] { _pendingPath, _pendingPath + ".tmp" })
        {
            try { File.Delete(path); } catch { }
        }
        try { Registry.CurrentUser.DeleteSubKeyTree(_registryPath, throwOnMissingSubKey: false); } catch { }
    }

    private string? ProfileOf(SecurityIdentifier student) => _profile;

    // A student pointing their Desktop at a folder that isn't theirs (C:\Windows, say).
    [Fact]
    public void ARefusedDesktop_StillGivesTheStudent()
    {
        var (desktop, student) = UserSessionProcessLauncher.StudentAndDesktop(Student, _outside, ProfileOf, out var refusal);

        Assert.Null(desktop);
        Assert.Equal(Student, student);
        Assert.Contains(_outside, refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADesktopInTheProfile_IsGivenWhereItReallyIs()
    {
        var shellDesktop = Path.Combine(_profile, "Desktop");

        var (desktop, student) = UserSessionProcessLauncher.StudentAndDesktop(Student, shellDesktop, ProfileOf, out var refusal);

        Assert.Equal(shellDesktop, desktop, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Student, student);
        Assert.Null(refusal);
    }

    // Windows had no Desktop for them: not a refusal, and still the student.
    [Fact]
    public void NoDesktopFound_StillGivesTheStudent()
    {
        var (desktop, student) = UserSessionProcessLauncher.StudentAndDesktop(Student, null, ProfileOf, out var refusal);

        Assert.Null(desktop);
        Assert.Equal(Student, student);
        Assert.Null(refusal);
    }

    // "Desinstalar programa" still sees the programs the student installed for themselves.
    [Fact]
    public void TheStudentsSoftwareHive_IsOpened_WhateverTheirDesktop()
    {
        var asked = new List<string>();

        using var hive = ProgramUninstaller.UserSoftwareOf((null, Student), sid =>
        {
            asked.Add(sid);
            return Registry.CurrentUser.CreateSubKey(_registryPath, writable: false);
        });

        Assert.NotNull(hive);
        Assert.Equal(new[] { Student.Value }, asked);
        Assert.Null(ProgramUninstaller.UserSoftwareOf(null, _ => throw new InvalidOperationException("nobody to open")));
    }

    // The removals the teacher left pending still run when such a student logs on.
    [Fact]
    public void PendingUninstalls_RunForAStudentWhoseDesktopIsRefused()
    {
        var pending = new PendingUninstalls(_pendingPath);
        pending.Add(new[] { "Discord" });
        var asked = new List<string>();
        var worker = new PendingUninstallWorker(
            PendingUninstallWorker.StudentSidOf(() => (null, Student)), pending, names =>
            {
                asked.AddRange(names);
                return names;
            });

        worker.RunOnce();

        Assert.Equal(new[] { "Discord" }, asked);
    }
}
