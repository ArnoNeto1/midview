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
