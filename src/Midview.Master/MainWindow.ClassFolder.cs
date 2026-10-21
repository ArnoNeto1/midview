using System.Collections.Concurrent;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// MainWindow: "Pasta da turma" (docs/superpowers/specs/2026-10-20-class-folder-design.md).
// One store and one sync for the window's whole life: the sync keeps the
// class folder the same on every PC while a lesson is on, whether or not its
// window is open, and the toolbar button shows the class under its label.
public partial class MainWindow
{
    private readonly ClassStore _classStore = new(
        ClassStore.DefaultFilesRoot, ClassStore.DefaultIndexRoot, new WindowsRecycleBin(), () => DateTime.Now);

    private readonly ClassFolderSync _classSync;

    // What the sync reads on its own threads, kept up to date on the UI thread:
    // the classroom list (the machines the class actions take), and whether
    // each machine answered its last poll, by its Session's thumbprint (the
    // tiles themselves are the UI thread's only).
    private IReadOnlyList<KnownMachine> _roster = Array.Empty<KnownMachine>();
    private readonly ConcurrentDictionary<string, bool> _online = new(StringComparer.OrdinalIgnoreCase);

    // Before the tiles: their pollers hand the lesson to each machine's Agent.
    private ClassFolderSync StartClassFolder()
    {
        var sync = new ClassFolderSync(
            _classStore, _masterCertificate, () => Volatile.Read(ref _roster), IsOnline, () => DateTime.Now);
        // Raised on the sync's threads, sometimes while it holds its lesson lock: only queued here.
        sync.LessonChanged += _ => Dispatcher.BeginInvoke(ShowClassLesson);
        return sync;
    }

    private bool IsOnline(KnownMachine machine) =>
        _online.TryGetValue(machine.SessionThumbprint, out var online) && online;

    // The class of the lesson on, under the button's label.
    private void ShowClassLesson()
    {
        var lesson = _classSync.Lesson;
        ClassFolderButton.Caption = lesson.IsOn ? lesson.Turma : string.Empty;
    }

    // After every change to _tiles.
    private void PublishRoster() => Volatile.Write(ref _roster, _tiles.Select(tile => tile.Machine).ToList());

    private void PublishPresence(MachineTile tile) =>
        _online[tile.Machine.SessionThumbprint] = tile.Presence == TilePresence.Online;

    // For the whole room, whichever machines are in it, like "Agendar".
    private void OnClassFolderClicked(object sender, RoutedEventArgs e) =>
        ShowOnce("pasta-da-turma", Array.Empty<KnownMachine>(), () => new ClassFolderWindow(
            _classStore, _classSync, () => Volatile.Read(ref _roster), IsOnline)
        {
            Owner = this,
        });
}
