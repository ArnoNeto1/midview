using Midview.Master;
using Xunit;

// ClassStore.AsideFolders: the folders a take-over left beside the classes
// ("Turmas (antes de …)", "Turmas (passadas para …)"), which the window
// doesn't show, so MainWindow says where they are.
public partial class ClassStoreTests
{
    [Fact]
    public void AsideFolders_AreTheOnesAHandOverLeftBesideTheClasses()
    {
        foreach (var name in new[]
                 {
                     "Turmas (passadas para PROF-2) (2)", "Turmas (antes de 20-10-2026 1330)", "Turmas (passadas para PROF-2)",
                     "Turmas velhas", "Turmas (recebendo)", "Outras (antes de 20-10-2026 1330)",
                 })
        {
            Directory.CreateDirectory(Path.Combine(_root, name));
        }
        File.WriteAllText(Path.Combine(_root, "Turmas (antes de 21-10-2026 0800).txt"), "não é uma pasta");

        Assert.Equal(
            new[] { "Turmas (antes de 20-10-2026 1330)", "Turmas (passadas para PROF-2)", "Turmas (passadas para PROF-2) (2)" },
            _store.AsideFolders());
    }

    [Fact]
    public void AsideFolders_WhereThereIsNothingBeside_AreNone()
    {
        var store = new Midview.Master.ClassStore(
            Path.Combine(_root, "nada", "Turmas"), IndexRoot, _bin, () => _now, _log.Add);

        Assert.Empty(store.AsideFolders());
    }

    // A PC that stopped being the home with no hand-over (a held lesson
    // ended, or it came back after a colleague took the folders over from it
    // while it was closed) keeps its store where it is: the room may name it
    // again. While it works another PC's folders (remote), the note says its
    // own classes are still in "Turmas" here, and that this window doesn't
    // show them; working its own, or with none, it says nothing of them.
    [Fact]
    public void TheAsideNote_OfARemotePcWithClassesOfItsOwn_SaysTheyStayInTurmas()
    {
        Assert.True(_store.Create("3º A").Ok);
        const string Where = @"C:\prof\Midview";

        Assert.Equal(ClassFolderText.AsideNote(Array.Empty<string>(), Where, ownClassesHidden: true),
            ClassFolderText.AsideNote(_store, remote: true, Where));
        Assert.Null(ClassFolderText.AsideNote(_store, remote: false, Where));
        Directory.CreateDirectory(Path.Combine(_root, "Turmas (antes de 20-10-2026 1330)"));
        Assert.Equal(ClassFolderText.AsideNote(new[] { "Turmas (antes de 20-10-2026 1330)" }, Where, ownClassesHidden: true),
            ClassFolderText.AsideNote(_store, remote: true, Where));

        var empty = new ClassStore(Path.Combine(_root, "nada", "Turmas"), IndexRoot, _bin, () => _now, _log.Add);
        Assert.Null(ClassFolderText.AsideNote(empty, remote: true, Where));
    }
}
