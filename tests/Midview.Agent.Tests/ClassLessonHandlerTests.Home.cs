using Midview.Agent;
using Midview.Protocol;
using Xunit;

// Two teacher PCs in one lab: only the class folders' home starts a lesson or
// changes the class; any trusted teacher ends it (decision 3).
public sealed partial class ClassLessonHandlerTests
{
    private const string HomePc = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string OtherPc = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private ClassHomeState HomeClaimedBy(string thumbprint, string name)
    {
        var home = new ClassHomeState(_homePath, _log.Add);
        Assert.True(home.Claim(thumbprint, "10.0.0.5", new ClassHomeClaim(name, ClassFolderRemote.Port, false)).Ok);
        return home;
    }

    [Fact]
    public void Handle_AStartFromAnotherTeacherPc_IsRefused_AndChangesNothing()
    {
        var home = HomeClaimedBy(HomePc, "PROF-1");
        var homeFile = File.ReadAllBytes(_homePath);
        var handler = Create(home: home);
        _inspector.Start("chrome.exe");

        var (ok, detail) = handler.Handle(Lesson("3º A", 100).Encode(), OtherPc, "10.0.0.7");

        Assert.False(ok);
        Assert.Equal("As pastas das turmas ficam no PC PROF-1", detail);
        Assert.False(handler.Current.IsOn);
        Assert.False(File.Exists(_statePath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_desktop));
        Assert.Null(Policy(DownloadFolderPolicy.ChromeKey));
        Assert.Null(Policy(DownloadFolderPolicy.EdgeKey));
        Assert.Empty(_inspector.Ended);
        Assert.Equal(homeFile, File.ReadAllBytes(_homePath));
    }

    [Fact]
    public void Handle_AClassChangeFromAnotherTeacherPc_IsRefused_AndTheLessonStays()
    {
        var handler = Create(home: HomeClaimedBy(HomePc, "PROF-1"));
        var lesson = Lesson("3º A", 100);
        Assert.True(handler.Handle(lesson.Encode(), HomePc, "10.0.0.5").Succeeded);
        var stateFile = File.ReadAllBytes(_statePath);
        _inspector.Start("chrome.exe");

        var (ok, detail) = handler.Handle(Lesson("3º B", 200).Encode(), OtherPc, "10.0.0.7");

        Assert.False(ok);
        Assert.Equal("As pastas das turmas ficam no PC PROF-1", detail);
        Assert.Equal(lesson, handler.Current);
        Assert.Equal(stateFile, File.ReadAllBytes(_statePath));
        Assert.False(Directory.Exists(FolderOf("3º B")));
        Assert.Equal(FolderOf("3º A"), Policy(DownloadFolderPolicy.ChromeKey));
        Assert.Empty(_inspector.Ended);
    }

    [Fact]
    public void Handle_AnEndFromAnotherTeacherPc_IsAccepted()
    {
        var home = HomeClaimedBy(HomePc, "PROF-1");
        var handler = Create(home: home);
        var lesson = Lesson("3º A", 100);
        Assert.True(handler.Handle(lesson.Encode(), HomePc, "10.0.0.5").Succeeded);
        var ended = lesson with { SavedAt = 101, Ended = true };

        var (ok, detail) = handler.Handle(ended.Encode(), OtherPc, "10.0.0.7");

        Assert.True(ok);
        Assert.Equal("aula encerrada", detail);
        Assert.Equal(ended, handler.Current);
        Assert.Equal(ended, ClassLessonFile.Read(_statePath));
        Assert.Null(Policy(DownloadFolderPolicy.ChromeKey));
        Assert.Equal(HomePc, home.Current!.Thumbprint);
    }

    // Decision 4: a Master that sent no claim (part 1) becomes the home by
    // starting the first lesson.
    [Fact]
    public void Handle_AStartWithNoHome_RecordsTheStarter()
    {
        var home = new ClassHomeState(_homePath, _log.Add);
        var handler = Create(home: home);

        var (ok, detail) = handler.Handle(Lesson("3º A", 100).Encode(), HomePc, "10.0.0.5");

        Assert.True(ok);
        Assert.Equal("aula do 3º A", detail);
        Assert.Equal(new ClassHome(HomePc, "", "10.0.0.5", ClassFolderRemote.Port), home.Current);

        var (otherOk, otherDetail) = handler.Handle(Lesson("3º B", 200).Encode(), OtherPc, "10.0.0.7");

        Assert.False(otherOk);
        Assert.Equal(ClassHomeClaim.OtherHomeText + "outro PC de professor", otherDetail);
    }

    [Fact]
    public void Handle_AnEndWithNoHome_RecordsNobody()
    {
        var home = new ClassHomeState(_homePath, _log.Add);
        var handler = Create(home: home);
        var ended = Lesson("3º A", 100) with { Ended = true };

        var (ok, _) = handler.Handle(ended.Encode(), HomePc, "10.0.0.5");

        Assert.True(ok);
        Assert.Null(home.Current);
        Assert.False(File.Exists(_homePath));
    }

    // A start refused for the lesson's own reasons records no home.
    [Fact]
    public void Handle_AnOlderStartWithNoHome_RecordsNobody()
    {
        ClassLessonFile.Write(_statePath, Lesson("3º A", 200) with { Ended = true });
        var home = new ClassHomeState(_homePath, _log.Add);
        var handler = Create(home: home);

        var (ok, detail) = handler.Handle(Lesson("3º B", 100).Encode(), HomePc, "10.0.0.5");

        Assert.True(ok);
        Assert.StartsWith(ClassLesson.KeptNewerText, detail);
        Assert.Null(home.Current);
    }
}
