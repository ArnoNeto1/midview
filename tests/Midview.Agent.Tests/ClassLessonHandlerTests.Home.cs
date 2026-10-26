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

    // An end ends only the lesson it is of: another teacher PC's lesson (a
    // lesson it held, ended; two starts at once), however new, leaves the one
    // on here as it is, folder, policy and Chrome included, and is answered as
    // an older lesson is (the sender's lesson is not the one followed here).
    [Theory]
    [InlineData(HomePc)]
    [InlineData(OtherPc)]
    public void Handle_AnEndOfAnotherLesson_IsNotTaken_AndTheLessonStaysOn(string sender)
    {
        var (handler, _) = HandlerWithHomeClaimedBy(HomePc, "PROF-1");
        var lesson = Lesson("3º A", 100);
        Assert.True(handler.Handle(lesson.Encode(), HomePc, "10.0.0.5").Succeeded);
        var stateFile = File.ReadAllBytes(_statePath);
        _inspector.Start("chrome.exe");
        var theirs = Lesson("3º B", 500) with { Ended = true };

        var (ok, detail) = handler.Handle(theirs.Encode(), sender, "10.0.0.7");

        Assert.True(ok);
        Assert.Equal($"{ClassLesson.KeptNewerText}: aula do 3º A", detail);
        Assert.Equal(lesson, handler.Current);
        Assert.Equal(stateFile, File.ReadAllBytes(_statePath));
        Assert.True(IsProtected(FolderOf("3º A")));
        Assert.Equal(FolderOf("3º A"), Policy(DownloadFolderPolicy.ChromeKey));
        Assert.Empty(_inspector.Ended);
    }

    // "No lesson" (an end with no lesson id), however new, is no end of the
    // lesson on here either: nothing changes, with or without a home.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Handle_NoLessonWhileALessonIsOn_IsNotTaken(bool withAHome)
    {
        var handler = withAHome ? HandlerWithHomeClaimedBy(HomePc, "PROF-1").Handler : Create();
        var lesson = Lesson("3º A", 100);
        Assert.True(handler.Handle(lesson.Encode(), HomePc, "10.0.0.5").Succeeded);
        var stateFile = File.ReadAllBytes(_statePath);

        var (ok, detail) = handler.Handle(ClassLesson.None(500).Encode(), OtherPc, "10.0.0.7");

        Assert.True(ok);
        Assert.Equal($"{ClassLesson.KeptNewerText}: aula do 3º A", detail);
        Assert.Equal(lesson, handler.Current);
        Assert.Equal(stateFile, File.ReadAllBytes(_statePath));
        Assert.Equal(FolderOf("3º A"), Policy(DownloadFolderPolicy.ChromeKey));
    }

    // A lesson held for the room (or taken back when another PC claimed the
    // room at the same moment) is ended one second after its own start; the
    // home's start, saved in that second or in the second of the ended
    // lesson's start, is taken over that end of another lesson rather than
    // refused as "saved in the same second" or as older (which the home's
    // poller would meet for ever). Only a start from the home, of another
    // lesson than the one ended: anything else is refused as before.
    [Theory]
    [InlineData(101)]
    [InlineData(100)]
    public void Handle_AStartFromTheHome_SavedWithinASecondOfAnotherLessonsEnd_IsTaken(long savedAt)
    {
        var (handler, _) = HandlerWithHomeClaimedBy(HomePc, "PROF-1");
        var held = Lesson("3º B", 100);
        Assert.True(handler.Handle(held.Encode(), HomePc, "10.0.0.5").Succeeded);
        var heldEnd = held with { SavedAt = 101, Ended = true };
        Assert.True(handler.Handle(heldEnd.Encode(), OtherPc, "10.0.0.7").Succeeded);
        var sameSecond = "outra aula foi salva no mesmo segundo; envie de novo";
        var keptNewer = $"{ClassLesson.KeptNewerText}: aula encerrada";

        Assert.Equal((false, sameSecond), handler.Handle((held with { SavedAt = 101 }).Encode(), HomePc, "10.0.0.5"));
        Assert.Equal((false, sameSecond), handler.Handle((Lesson("3º C", 101) with { Ended = true }).Encode(), HomePc, "10.0.0.5"));
        Assert.Equal((true, keptNewer), handler.Handle(Lesson("3º C", 99).Encode(), HomePc, "10.0.0.5"));
        Assert.Equal((true, keptNewer), handler.Handle((Lesson("3º C", 100) with { Ended = true }).Encode(), HomePc, "10.0.0.5"));
        Assert.Equal(heldEnd, handler.Current);

        var homes = Lesson("3º A", savedAt);
        var (ok, detail) = handler.Handle(homes.Encode(), HomePc, "10.0.0.5");

        Assert.True(ok, detail);
        Assert.Equal(homes, handler.Current);
        Assert.Equal(homes, ClassLessonFile.Read(_statePath));
        Assert.True(IsProtected(FolderOf("3º A")));
    }

    // Another teacher PC's start in those seconds is still refused, naming the
    // home; and with no home kept (part 1), the order is by SavedAt alone.
    [Fact]
    public void Handle_AStartFromAnotherPcOrWithNoHome_SavedWithinASecondOfAnEnd_IsStillRefused()
    {
        var (handler, _) = HandlerWithHomeClaimedBy(HomePc, "PROF-1");
        var held = Lesson("3º B", 100);
        Assert.True(handler.Handle(held.Encode(), HomePc, "10.0.0.5").Succeeded);
        var heldEnd = held with { SavedAt = 101, Ended = true };
        Assert.True(handler.Handle(heldEnd.Encode(), HomePc, "10.0.0.5").Succeeded);

        Assert.Equal((false, "As pastas das turmas ficam no PC PROF-1"), handler.Handle(Lesson("3º A", 101).Encode(), OtherPc, "10.0.0.7"));
        Assert.Equal(heldEnd, handler.Current);

        var partOne = Create(); // the same lesson file, no home
        Assert.Equal(heldEnd, partOne.Current);
        Assert.Equal((false, "outra aula foi salva no mesmo segundo; envie de novo"), partOne.Handle(Lesson("3º A", 101).Encode()));
        Assert.Equal((true, $"{ClassLesson.KeptNewerText}: aula encerrada"), partOne.Handle(Lesson("3º A", 100).Encode()));
        Assert.Equal(heldEnd, partOne.Current);
    }

    // With no lesson on, an end of any lesson is taken, newest first, as before.
    [Fact]
    public void Handle_WithNoLessonOn_AnEndOfAnyLesson_IsTaken()
    {
        var (handler, _) = HandlerWithHomeClaimedBy(HomePc, "PROF-1");
        var lesson = Lesson("3º A", 100);
        Assert.True(handler.Handle(lesson.Encode(), HomePc, "10.0.0.5").Succeeded);
        Assert.True(handler.Handle((lesson with { SavedAt = 101, Ended = true }).Encode(), HomePc, "10.0.0.5").Succeeded);
        var theirs = Lesson("3º B", 500) with { Ended = true };

        var (ok, detail) = handler.Handle(theirs.Encode(), OtherPc, "10.0.0.7");

        Assert.True(ok);
        Assert.Equal("aula encerrada", detail);
        Assert.Equal(theirs, handler.Current);
        Assert.Equal(theirs, ClassLessonFile.Read(_statePath));
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

    // Ruling 22 (Task 7a, item 9): a lesson set waits for the lesson's lock no
    // longer than a claim does, and is refused the same way (the Master's
    // poller sends it again), never applied later.
    [Fact]
    public async Task Handle_HeldBehindTheLessonsLock_IsRefusedAsBusy_AndNeverAppliedLater()
    {
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
            _log.Add, claimWait: TimeSpan.FromMilliseconds(300));
        Volatile.Write(ref hold, 1);
        var reconciling = Task.Run(handler.Reconcile);
        Assert.True(holding.Wait(TimeSpan.FromSeconds(10)));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (ok, detail) = handler.Handle(Lesson("3º A", 100).Encode(), HomePc, "10.0.0.5");
        clock.Stop();
        Volatile.Write(ref hold, 0);
        release.Set();
        await reconciling.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(ok);
        Assert.Equal(ClassHomeClaim.BusyText, detail);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        Assert.False(handler.Current.IsOn);
        Assert.False(File.Exists(_statePath));
        // With the lock free, the same lesson is taken.
        Assert.True(handler.Handle(Lesson("3º A", 100).Encode(), HomePc, "10.0.0.5").Succeeded);
        Assert.Equal("3º A", handler.Current.Turma);
    }

    // Ruling 19: the Agent waits for its lock as long as the Master counts on
    // (ClassHomeClaim.MaxLockWait; the Master's tests check it against its read timeout).
    [Fact]
    public void TheClaimWait_IsTheOneTheMasterCountsOn()
    {
        Assert.Equal(ClassHomeClaim.MaxLockWait, ClassLessonHandler.DefaultClaimWait);
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
