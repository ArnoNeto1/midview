using Microsoft.Win32;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// Two teacher PCs in one lab: only the class folders' home starts a lesson or
// changes the class; any trusted teacher ends it (decision 3). Claims go
// through the handler, under the lesson's lock.
public sealed partial class ClassLessonHandlerTests
{
    private const string HomePc = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string OtherPc = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static ClassHomeClaim ClaimOf(string name, bool takeOver = false, long generation = 100) =>
        new(name, ClassFolderRemote.Port, takeOver, generation);

    private (ClassLessonHandler Handler, ClassHomeState Home) HandlerWithHomeClaimedBy(string thumbprint, string name)
    {
        var home = new ClassHomeState(_homePath, _log.Add);
        var handler = Create(home: home);
        Assert.True(handler.ClaimHome(thumbprint, "10.0.0.5", ClaimOf(name)).Ok);
        return (handler, home);
    }

    [Fact]
    public void Handle_AStartFromAnotherTeacherPc_IsRefused_AndChangesNothing()
    {
        var (handler, _) = HandlerWithHomeClaimedBy(HomePc, "PROF-1");
        var homeFile = File.ReadAllBytes(_homePath);
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

    // The overload with no sender identity is refused like any other PC.
    [Fact]
    public void Handle_WithNoSender_IsRefusedOnceAHomeExists()
    {
        var (handler, _) = HandlerWithHomeClaimedBy(HomePc, "PROF-1");

        var (ok, detail) = handler.Handle(Lesson("3º A", 100).Encode());

        Assert.False(ok);
        Assert.Equal("As pastas das turmas ficam no PC PROF-1", detail);
        Assert.False(File.Exists(_statePath));
    }

    [Fact]
    public void Handle_AClassChangeFromAnotherTeacherPc_IsRefused_AndTheLessonStays()
    {
        var (handler, _) = HandlerWithHomeClaimedBy(HomePc, "PROF-1");
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
        var (handler, home) = HandlerWithHomeClaimedBy(HomePc, "PROF-1");
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
        Assert.Equal(new ClassHome(HomePc, "", "10.0.0.5", ClassFolderRemote.Port, 0), handler.CurrentHome);

        var (otherOk, otherDetail) = handler.Handle(Lesson("3º B", 200).Encode(), OtherPc, "10.0.0.7");

        Assert.False(otherOk);
        Assert.Equal("As pastas das turmas ficam no PC de outro professor", otherDetail);
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

    // Ruling 5: the home PC with another certificate (reinstalled, paired
    // again, another Windows account) is still the home, and starts lessons.
    [Fact]
    public void ClaimHome_TheHomesNameWithAnotherThumbprint_ThenStartsALesson()
    {
        var (handler, home) = HandlerWithHomeClaimedBy(HomePc, "PROF-1");

        var (ok, detail) = handler.ClaimHome(OtherPc, "10.0.0.8", ClaimOf("prof-1"));
        var (started, _) = handler.Handle(Lesson("3º A", 100).Encode(), OtherPc, "10.0.0.8");
        var (oldCertificate, oldDetail) = handler.Handle(Lesson("3º B", 200).Encode(), HomePc, "10.0.0.5");

        Assert.True(ok);
        Assert.Equal("este PC guarda as pastas", detail);
        Assert.Equal(new ClassHome(OtherPc, "PROF-1", "10.0.0.8", ClassFolderRemote.Port, 100), home.Current);
        Assert.True(started);
        Assert.False(oldCertificate);
        Assert.Equal("As pastas das turmas ficam no PC PROF-1", oldDetail);
    }

    [Fact]
    public void ClaimHome_AnotherNameWithAnotherThumbprint_IsRefused()
    {
        var (handler, home) = HandlerWithHomeClaimedBy(HomePc, "PROF-1");

        var (ok, detail) = handler.ClaimHome(OtherPc, "10.0.0.8", ClaimOf("PROF-2"));

        Assert.False(ok);
        Assert.Equal("As pastas das turmas ficam no PC PROF-1", detail);
        Assert.Equal(HomePc, home.Current!.Thumbprint);
    }

    // Plan decision 7: a TakeOver waits for the lesson's end.
    [Fact]
    public void ClaimHome_TakeOverWhileALessonIsOn_IsRefused_UntilItEnds()
    {
        var (handler, home) = HandlerWithHomeClaimedBy(HomePc, "PROF-1");
        var lesson = Lesson("3º A", 100);
        handler.Handle(lesson.Encode(), HomePc, "10.0.0.5");

        var during = handler.ClaimHome(OtherPc, "10.0.0.7", ClaimOf("PROF-2", takeOver: true, generation: 200));
        handler.Handle((lesson with { SavedAt = 101, Ended = true }).Encode(), OtherPc, "10.0.0.7");
        var after = handler.ClaimHome(OtherPc, "10.0.0.7", ClaimOf("PROF-2", takeOver: true, generation: 200));

        Assert.Equal((false, "Encerre a aula antes de passar as pastas."), during);
        Assert.Equal((true, "as pastas passaram para este PC"), after);
        Assert.Equal(new ClassHome(OtherPc, "PROF-2", "10.0.0.7", ClassFolderRemote.Port, 200), home.Current);
    }

    // Ruling 18: a claim that can't have the lesson's lock within the claim
    // wait (a look at the student's folders holds it: closing the browsers, the
    // registry) is refused as busy, quickly, and never applied later. A Master
    // whose answer timed out reads the home again, and must find it as it is.
    [Fact]
    public async Task ClaimHome_HeldBehindTheLessonsLock_IsRefusedAsBusy_AndNeverAppliedLater()
    {
        var home = new ClassHomeState(_homePath, _log.Add);
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var hold = 0;
        var handler = new ClassLessonHandler(
            _statePath,
            () =>
            {
                if (Volatile.Read(ref hold) == 1)
                {
                    holding.Set();
                    release.Wait(TimeSpan.FromSeconds(30));
                }
                return (_desktop, Me);
            },
            _ => Registry.CurrentUser.CreateSubKey(_rootPath, writable: true), new AppBlockEnforcer(_inspector), Me, () => _now,
            _log.Add, home, claimWait: TimeSpan.FromMilliseconds(300));
        Assert.True(handler.ClaimHome(HomePc, "10.0.0.5", ClaimOf("PROF-1")).Ok);
        var before = home.Current;
        Volatile.Write(ref hold, 1);
        var reconciling = Task.Run(handler.Reconcile);
        Assert.True(holding.Wait(TimeSpan.FromSeconds(10)));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (ok, detail) = handler.ClaimHome(OtherPc, "10.0.0.7", ClaimOf("PROF-2", takeOver: true, generation: 200));
        clock.Stop();
        Volatile.Write(ref hold, 0);
        release.Set();
        await reconciling.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(ok);
        Assert.Equal(ClassHomeClaim.BusyText, detail);
        Assert.False(detail.StartsWith(ClassHomeClaim.OtherHomeText, StringComparison.Ordinal));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        Assert.Equal(before, handler.CurrentHome);
        Assert.Equal(before, new ClassHomeState(_homePath, _log.Add).Current);
        // With the lock free, the same claim is taken.
        Assert.True(handler.ClaimHome(OtherPc, "10.0.0.7", ClaimOf("PROF-2", takeOver: true, generation: 200)).Ok);
        Assert.Equal("PROF-2", handler.CurrentHome!.Name);
    }

    // The claim waits well under a Master's read timeout (20 s) by default.
    [Fact]
    public void TheClaimWait_IsWellUnderTheMastersReadTimeout()
    {
        Assert.True(ClassLessonHandler.DefaultClaimWait <= TimeSpan.FromSeconds(10));
        Assert.True(ClassLessonHandler.DefaultClaimWait > TimeSpan.Zero);
    }

    [Fact]
    public void WithNoHomeState_TheHandlerKeepsNoHome()
    {
        var handler = Create();

        Assert.False(handler.KeepsHome);
        Assert.Null(handler.CurrentHome);
        Assert.False(handler.ClaimHome(HomePc, "10.0.0.5", ClaimOf("PROF-1")).Ok);
        Assert.True(handler.Handle(Lesson("3º A", 100).Encode(), OtherPc, "10.0.0.7").Succeeded);
    }
}
