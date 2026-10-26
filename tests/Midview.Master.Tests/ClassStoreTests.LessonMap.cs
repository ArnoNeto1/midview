using Midview.Master;
using Xunit;

public partial class ClassStoreTests
{
    // --- Which class each lesson was of ---

    private string LessonsFile => Path.Combine(_root, "class-lessons.json");

    [Fact]
    public void LessonMap_RemembersTheClassOfALesson_AndWhetherItWasCollected_AcrossRestarts()
    {
        MakeClass();
        var lesson = Guid.NewGuid();
        var other = Guid.NewGuid();

        _store.RememberLesson(lesson, Turma);
        _store.RememberLesson(other, Turma);
        _store.MarkCollected(lesson);

        var reopened = NewStore(_bin);
        Assert.Equal(Turma, reopened.TurmaOfLesson(lesson));
        Assert.True(reopened.IsCollected(lesson));
        Assert.Equal(Turma, reopened.TurmaOfLesson(other));
        Assert.False(reopened.IsCollected(other));
    }

    [Fact]
    public void LessonMap_AnUnknownLesson_HasNoClass_AndIsNotCollected()
    {
        Assert.Null(_store.TurmaOfLesson(Guid.NewGuid()));
        Assert.False(_store.IsCollected(Guid.NewGuid()));
        Assert.False(File.Exists(LessonsFile));
    }

    // That the defaults are both in MasterData.Folder is MasterDataTests' to say:
    // it moves the folder for a moment, so no other class reads it.
    [Fact]
    public void LessonMap_ByDefault_LivesNextToTheIndexFolder_NotInIt()
    {
        MakeClass("lessons"); // a class with that name has its own index in the index folder
        Write("lessons", "a.txt");
        _store.RefreshTombstones("lessons");
        _store.RememberLesson(Guid.NewGuid(), "lessons");

        Assert.True(File.Exists(LessonsFile));
        Assert.Equal(new[] { "lessons.json" }, Directory.GetFiles(IndexRoot).Select(file => Path.GetFileName(file)));
        Assert.NotNull(NewStore(_bin).ArrivedOn("lessons", "a.txt"));
    }

    [Fact]
    public void LessonMap_KnownVersions_AreTheClassesFilesAndThePcsRecords_ComparedIgnoringCase()
    {
        MakeClass();
        Write(Turma, "fica.txt", "a");
        Commit(Turma, "apagado.txt", "bb");
        _store.RecordSynced(Turma, "PC1", "apagado.txt", VersionOf("bb"));
        File.Delete(Path.Combine(FolderOf(Turma), "apagado.txt"));
        var lesson = Guid.NewGuid();

        _store.RememberVersionsOf(lesson, Turma);
        _store.RememberVersion(lesson, "novo.txt", VersionOf("ccc"));

        var reopened = NewStore(_bin);
        Assert.True(reopened.IsKnownVersion(lesson, "FICA.txt", VersionOf("a")));
        Assert.True(reopened.IsKnownVersion(lesson, "apagado.txt", VersionOf("bb")));
        Assert.True(reopened.IsKnownVersion(lesson, "novo.txt", VersionOf("ccc")));
        Assert.False(reopened.IsKnownVersion(lesson, "fica.txt", VersionOf("a", DefaultWriteUtc.AddMinutes(1))));
        Assert.False(reopened.IsKnownVersion(Guid.NewGuid(), "fica.txt", VersionOf("a")));
    }

    [Fact]
    public void LessonMap_FollowsARenamedClass_AndForgetsADeletedOne()
    {
        MakeClass();
        MakeClass("3º B");
        var lessonA = Guid.NewGuid();
        var lessonB = Guid.NewGuid();
        _store.RememberLesson(lessonA, Turma);
        _store.RememberLesson(lessonB, "3º B");

        Assert.True(_store.Rename(Turma, "3º C").Ok);
        Assert.True(_store.Delete("3º B").Ok);

        var reopened = NewStore(_bin);
        Assert.Equal("3º C", reopened.TurmaOfLesson(lessonA));
        Assert.Null(reopened.TurmaOfLesson(lessonB));
    }

    [Fact]
    public void LessonMap_ANewClassWithAnOldName_DoesNotInheritItsLessons()
    {
        MakeClass();
        var lesson = Guid.NewGuid();
        _store.RememberLesson(lesson, Turma);
        Directory.Delete(FolderOf(Turma)); // deleted in Explorer, then made again

        MakeClass();

        Assert.Null(_store.TurmaOfLesson(lesson));
    }

    // A lesson of a class deleted here is still known (what a PC set aside for
    // it is cleared), unlike one never known (another teacher PC's, left alone).
    [Fact]
    public void LessonMap_ALessonOfADeletedClass_StaysKnown_WithNoClass()
    {
        MakeClass();
        MakeClass("3º B");
        var deleted = Guid.NewGuid();
        var renamedOver = Guid.NewGuid();
        _store.RememberLesson(deleted, "3º B");
        _store.RememberLesson(renamedOver, Turma);
        _store.RememberVersion(deleted, "a.txt", VersionOf("a"));
        Assert.True(_store.Delete("3º B").Ok);
        Directory.Delete(FolderOf(Turma));
        MakeClass(); // a new class under the old name

        var reopened = NewStore(_bin);
        foreach (var lesson in new[] { deleted, renamedOver })
        {
            Assert.True(reopened.KnowsLesson(lesson));
            Assert.True(reopened.RanLesson(lesson));
            Assert.Null(reopened.TurmaOfLesson(lesson));
        }
        Assert.False(reopened.IsKnownVersion(deleted, "a.txt", VersionOf("a")));
        Assert.False(reopened.KnowsLesson(Guid.NewGuid()));
        Assert.False(reopened.RanLesson(Guid.NewGuid()));
    }

    // A lesson only heard of from the room's Agents: known, with its class,
    // but not one this PC ran; a lesson already known stays as it was.
    [Fact]
    public void LessonMap_ALessonHeardOf_IsKnown_ButNotRun_AndNeverReplacesOneKnown()
    {
        MakeClass();
        var heard = Guid.NewGuid();
        var ours = Guid.NewGuid();
        _store.RememberLesson(ours, Turma);
        _store.MarkCollected(ours);

        _store.RememberLessonHeardOf(heard, Turma);
        _store.RememberLessonHeardOf(ours, Turma);

        var reopened = NewStore(_bin);
        Assert.True(reopened.KnowsLesson(heard));
        Assert.False(reopened.RanLesson(heard));
        Assert.Equal(Turma, reopened.TurmaOfLesson(heard));
        Assert.False(reopened.IsCollected(heard));
        Assert.True(reopened.RanLesson(ours));
        Assert.True(reopened.IsCollected(ours));
        Assert.Throws<ArgumentException>(() => _store.RememberLessonHeardOf(Guid.NewGuid(), "..\\fora"));
    }

    // A take-over's swap puts the received lessons map in place of this PC's
    // own, which goes aside with this PC's classes: the two are merged, so no
    // lesson this PC ran is forgotten. A lesson this PC ran wins over the same
    // one only heard of in the received map. One whose class didn't come with
    // the received classes stays known, but what PCs set aside for it is left
    // on them: its class went aside, it wasn't deleted.
    [Fact]
    public void LessonMap_ATakeOversSwap_MergesThisPcsOwnLessonsIntoTheReceivedOnes()
    {
        MakeClass();
        MakeClass("Só aqui");
        var older = Guid.NewGuid();
        var heard = Guid.NewGuid();
        var onlyHere = Guid.NewGuid();
        var collected = Guid.NewGuid();
        _store.RememberLesson(older, Turma);
        _store.RememberLesson(heard, Turma);
        _store.RememberLesson(onlyHere, "Só aqui");
        _store.RememberLesson(collected, "Só aqui");
        _store.MarkCollected(collected);
        var staging = _store.Staging;
        Directory.CreateDirectory(Path.Combine(staging.Files, Turma));
        var received = new ClassStore(staging.Files, staging.Index, _bin, () => _now, _log.Add, lessonsPath: staging.Lessons);
        var theirs = Guid.NewGuid();
        received.RememberLessonHeardOf(heard, Turma);
        received.RememberLesson(theirs, Turma);

        _store.SwapIn("antes de 20-10-2026 1330");

        var reopened = NewStore(_bin);
        Assert.Equal(new[] { Turma }, reopened.ClassNames());
        Assert.All(new[] { older, heard, onlyHere, collected, theirs }, lesson => Assert.True(reopened.RanLesson(lesson)));
        Assert.All(new[] { older, heard, collected, theirs }, lesson => Assert.True(reopened.CollectsSetAsideOf(lesson)));
        Assert.False(reopened.CollectsSetAsideOf(onlyHere));
        Assert.Equal(Turma, reopened.TurmaOfLesson(older));
        Assert.True(reopened.IsCollected(collected));
        Assert.False(reopened.CollectsSetAsideOf(Guid.NewGuid()));
    }

    // A take-over's swap undone (the room didn't take this PC), with or
    // without a lessons map in what was received: everything goes back, and
    // this PC's own map is as it was, its lessons ran and collected as before.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LessonMap_ATakeOversSwapUndone_PutsThisPcsOwnMapBackWhole(bool receivedAMap)
    {
        MakeClass();
        var own = Guid.NewGuid();
        _store.RememberLesson(own, Turma);
        var staging = _store.Staging;
        Directory.CreateDirectory(Path.Combine(staging.Files, "Outra"));
        if (receivedAMap)
        {
            var received = new ClassStore(staging.Files, staging.Index, _bin, () => _now, _log.Add, lessonsPath: staging.Lessons);
            received.RememberLesson(Guid.NewGuid(), "Outra");
        }
        Assert.Equal(receivedAMap, File.Exists(staging.Lessons));

        var putBack = _store.SwapIn("antes de 20-10-2026 1330").Undo();

        Assert.True(putBack.All, string.Join("\n", _log));
        var reopened = NewStore(_bin);
        Assert.Equal(new[] { Turma }, reopened.ClassNames());
        Assert.True(reopened.RanLesson(own));
        Assert.True(reopened.CollectsSetAsideOf(own));
        Assert.Equal(Turma, reopened.TurmaOfLesson(own));
    }

    [Fact]
    public void LessonMap_ACorruptFile_IsSetAside_AndTheMapStartsEmpty()
    {
        MakeClass();
        File.WriteAllText(LessonsFile, "{ not json");

        var store = NewStore(_bin);
        Assert.Null(store.TurmaOfLesson(Guid.NewGuid()));
        var lesson = Guid.NewGuid();
        store.RememberLesson(lesson, Turma);

        Assert.True(File.Exists(LessonsFile + ".corrupt"));
        Assert.Equal(Turma, NewStore(_bin).TurmaOfLesson(lesson));
    }

    [Fact]
    public void LessonMap_RefusesANameThatIsNoClassName()
    {
        Assert.Throws<ArgumentException>(() => _store.RememberLesson(Guid.NewGuid(), "..\\fora"));
    }
}
