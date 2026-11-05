using Microsoft.Win32;
using Midview.Protocol;
using Xunit;

// What goes wrong around ClassSaveLocations: a target that can't be read, an
// untrusted dialog history, a state lost across lessons, a shell that stores
// Downloads in another form.
public sealed partial class ClassSaveLocationsTests
{
    private const string Desktop7A = @"%USERPROFILE%\Desktop\Pasta da turma – 7A";

    private SavedSetting DownloadsInState() =>
        ClassSaveLocationsFile.Read(_statePath, out _).Settings.Single(s =>
            s.Key == ClassSaveTargets.DownloadsKey && s.Name == ClassSaveTargets.DownloadsName);

    // Notepad and Paint are left alone, and why is said once, not every few seconds.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UntrustedMruOrder_IsLoggedOnce(bool binary)
    {
        using (var key = _software.CreateSubKey(ClassSaveTargets.MruKey))
        {
            if (binary)
            {
                key.SetValue(LastVisitedMru.OrderName, new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
            }
            else
            {
                key.SetValue(LastVisitedMru.OrderName, "1", RegistryValueKind.String);
            }
        }
        var follower = New();

        follower.Follow();
        _lesson = Lesson(3, "8B");
        Directory.CreateDirectory(Folder("8B"));
        follower.Follow();

        Assert.Equal(Folder("8B"), Downloads());
        Assert.Null(Bytes("0"));
        Assert.Single(_log, line => line.Contains(LastVisitedMru.OrderName));
    }

    [Fact]
    public void NoIdListForTheFolder_SkipsNotepadAndPaint_AndSaysSo()
    {
        _shell.IdLists = _ => null;

        New().Follow();

        Assert.Equal(Folder("7A"), Downloads());
        Assert.Null(Bytes("0"));
        Assert.Null(Bytes(LastVisitedMru.OrderName));
        Assert.Single(_log, line => line.Contains("ID list"));
    }

    // One target that throws costs only that target, every time.
    [Fact]
    public void IdListThrows_DownloadsAndOfficeStillApply()
    {
        _shell.IdLists = _ => throw new UnauthorizedAccessException("denied");

        New().Follow();

        Assert.Equal(Folder("7A"), Downloads());
        Assert.Equal(Folder("7A"), Office(0, "DOC-PATH"));
        Assert.Equal(Folder("7A"), Office(1, "DefaultPath"));
        Assert.Equal(0, PreferCloud());
        Assert.Null(Bytes("0"));
        Assert.Equal(5, ClassSaveLocationsFile.Read(_statePath, out _).Settings.Count);
        Assert.Single(_log, line => line.Contains("denied"));
    }

    // A state lost in an earlier lesson leaves that lesson's folder in place, and
    // putting it back would point at a folder already in the Recycle Bin.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BeforeIsAnEarlierClassFolder_PutsBackTheDefault(bool unexpanded)
    {
        var earlier = unexpanded ? @"%USERPROFILE%\Desktop\Pasta da turma – 6Z\" : Path.Combine(_desktop, "Pasta da turma – 6Z");
        StudentSetsDownloads(earlier);
        using (var key = _software.CreateSubKey(ClassSaveTargets.OfficeFolders[0].Key))
        {
            key.SetValue("DOC-PATH", earlier, RegistryValueKind.ExpandString);
        }
        var follower = New();
        follower.Follow();
        Assert.Equal(Folder("7A"), Downloads());

        _lesson = ClassLesson.None(2);
        follower.Follow();

        Assert.Equal(Original, Downloads());
        Assert.Null(Office(0, "DOC-PATH"));
    }

    // The shell might store Downloads in another form than it was given (Windows
    // 10 was not checked): the undo must still know it for Midview's.
    [Fact]
    public void ShellStoresDownloadsInAnotherForm_TheEndStillPutsItBack()
    {
        _shell.Stores = path => path == Folder("7A") ? Desktop7A : path;

        New().Follow();
        Assert.Equal(Desktop7A, Downloads());
        Assert.Equal(Desktop7A, DownloadsInState().Written);

        _lesson = ClassLesson.None(2);
        New().Follow();

        Assert.Equal(Original, Downloads());
    }
}
