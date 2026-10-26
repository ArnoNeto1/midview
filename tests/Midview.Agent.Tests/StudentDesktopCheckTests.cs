using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Midview.Agent;
using Xunit;

// The Desktop path is the student's to set (HKCU User Shell Folders),
// so the Agent, as SYSTEM, only works on a Desktop that really is in the
// student's profile, or is the student's own folder. Against real folders under
// %TEMP%: a made-up "profile", a place outside it, and junctions between them.
public sealed class StudentDesktopCheckTests : IDisposable
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;

    // An account that owns nothing here.
    private static readonly SecurityIdentifier SomeoneElse = new("S-1-5-21-1111111111-2222222222-3333333333-1001");

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-desktopcheck-{Guid.NewGuid():N}");
    private readonly string _profile;
    private readonly string _outside;
    private readonly List<string> _links = new();

    public StudentDesktopCheckTests()
    {
        _profile = Path.Combine(_root, "perfil");
        _outside = Path.Combine(_root, "outra conta");
        Directory.CreateDirectory(Path.Combine(_profile, "Desktop"));
        Directory.CreateDirectory(Path.Combine(_outside, "Desktop"));
    }

    public void Dispose()
    {
        foreach (var link in _links.Where(Directory.Exists))
        {
            Directory.Delete(link);
        }
        GiveBack(_root);
        Directory.Delete(_root, recursive: true);
    }

    // Each folder opened to whoever runs the tests (its owner) before looking inside it.
    private static void GiveBack(string directory)
    {
        var security = new DirectorySecurity();
        security.AddAccessRule(new FileSystemAccessRule(Me, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(security);
        foreach (var subdirectory in Directory.EnumerateDirectories(directory))
        {
            GiveBack(subdirectory);
        }
    }

    private string? ProfileOf(SecurityIdentifier student) => _profile;

    private static SecurityIdentifier OwnerOf(string folder) =>
        (SecurityIdentifier)new DirectoryInfo(folder).GetAccessControl().GetOwner(typeof(SecurityIdentifier))!;

    [Fact]
    public void ADesktopInTheProfile_IsUsed()
    {
        var desktop = Path.Combine(_profile, "Desktop");

        var verified = StudentDesktopCheck.Verify(desktop, SomeoneElse, ProfileOf, out var refusal);

        Assert.Equal(desktop, verified, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(refusal);
    }

    [Fact]
    public void ADesktopOutsideTheProfile_ThatIsNotTheStudents_IsRefused()
    {
        var desktop = Path.Combine(_outside, "Desktop");

        Assert.Null(StudentDesktopCheck.Verify(desktop, SomeoneElse, ProfileOf, out var refusal));
        Assert.Contains("outside", refusal);
    }

    [Fact]
    public void ADesktopOutsideTheProfile_ThatIsTheStudentsOwn_IsUsed()
    {
        var desktop = Path.Combine(_outside, "Desktop");

        Assert.Equal(desktop, StudentDesktopCheck.Verify(desktop, OwnerOf(desktop), ProfileOf, out _), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithNoProfileKnown_OnlyTheOwnerVouchesForIt()
    {
        var desktop = Path.Combine(_profile, "Desktop");

        Assert.Null(StudentDesktopCheck.Verify(desktop, SomeoneElse, _ => null, out _));
        Assert.NotNull(StudentDesktopCheck.Verify(desktop, OwnerOf(desktop), _ => null, out _));
    }

    // The path names the profile; the folder it reaches is another account's.
    [Fact]
    public void ADesktopReachedThroughALinkInTheProfile_GoesByWhereItReallyIs()
    {
        MakeJunction(Path.Combine(_profile, "atalho"), _outside);
        var desktop = Path.Combine(_profile, "atalho", "Desktop");

        Assert.Null(StudentDesktopCheck.Verify(desktop, SomeoneElse, ProfileOf, out var refusal));
        Assert.Contains(Path.Combine(_outside, "Desktop"), refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADesktopThatIsItselfALink_IsRefused()
    {
        var desktop = Path.Combine(_profile, "Desktop2");
        MakeJunction(desktop, Path.Combine(_outside, "Desktop"));

        Assert.Null(StudentDesktopCheck.Verify(desktop, OwnerOf(_outside), ProfileOf, out _));
    }

    // A Desktop path that no longer leads where it did (a link put in its way after the check):
    // nothing is created, protected or released through it.
    [Fact]
    public void Protect_ThroughAPathThatIsNotWhereTheDesktopReallyIs_DoesNothing()
    {
        MakeJunction(Path.Combine(_profile, "atalho"), _outside);
        var desktop = Path.Combine(_profile, "atalho", "Desktop");
        var real = Path.Combine(_outside, "Desktop");

        var problems = ProtectedStudentFolders.Protect(desktop, Me, Me, new[] { "Pasta da turma – 3º A" });

        Assert.NotEmpty(problems);
        Assert.Empty(Directory.GetFileSystemEntries(real));
        Assert.DoesNotContain(new DirectoryInfo(real).GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>(), rule => rule.AccessControlType == AccessControlType.Deny);
    }

    [Fact]
    public void Release_ThroughAPathThatIsNotWhereTheDesktopReallyIs_DoesNothing()
    {
        var real = Path.Combine(_outside, "Desktop");
        var name = "Pasta da turma – 3º A";
        Assert.Empty(ProtectedStudentFolders.Protect(real, Me, Me, new[] { name }));
        MakeJunction(Path.Combine(_profile, "atalho"), _outside);
        var desktop = Path.Combine(_profile, "atalho", "Desktop");

        var problems = ProtectedStudentFolders.Release(desktop, SomeoneElse, new[] { name });

        Assert.NotEmpty(problems);
        var security = new DirectoryInfo(Path.Combine(real, name)).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected); // still the protection Protect gave it
        Assert.DoesNotContain(security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            rule => rule.IdentityReference.Equals(SomeoneElse));
    }

    private void MakeJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), "mklink /J did not make the junction");
        _links.Add(link);
    }
}
