using Xunit;

// ClassStore.AsideFolders: the folders a take-over left beside the classes
// ("Turmas (antes de …)", "Turmas (passadas para …)"), which the window
// doesn't show, so MainWindow says where they are once (Task 7).
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
}
