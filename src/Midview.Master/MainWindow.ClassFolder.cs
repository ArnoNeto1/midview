using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// MainWindow: "Pasta da turma" (docs/superpowers/specs/2026-10-20-class-folder-design.md).
// One store and one sync for the window's whole life: the sync keeps the
// class folder the same on every PC while a lesson is on, whether or not its
// window is open, and the toolbar button shows the class under its label.
// Which teacher PC keeps the class folders, and so what the window works
// (this PC's own store, or the home's through a remote controller), is
// ClassFolderRole's (MainWindow.ClassHome.cs).
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

    // The window while it is open (one at a time: ShowOnce). UI thread only.
    private ClassFolderWindow? _classWindow;

    // Where the classes a take-over left aside are, and, while this PC works
    // another PC's folders, that its own stay in its "Turmas"
    // (ClassFolderText.AsideNote): read at the start, after every take-over
    // and at every change of the role, said in the window; null for none.
    private string? _classAsideNote;

    // Before the tiles: their pollers hand the lesson to each machine's Agent.
    private ClassFolderSync StartClassFolder()
    {
        var sync = new ClassFolderSync(
            _classStore, _masterCertificate, () => Volatile.Read(ref _roster), IsOnline, () => DateTime.Now,
            Environment.MachineName);
        // Raised on the sync's threads, sometimes while it holds its lesson lock: only queued here.
        sync.LessonChanged += _ => Dispatcher.BeginInvoke(ShowClassLesson);
        return sync;
    }

    private bool IsOnline(KnownMachine machine) =>
        _online.TryGetValue(machine.SessionThumbprint, out var online) && online;

    // The class of the lesson on, under the button's label: this PC's, or,
    // when another teacher PC keeps the class folders, that PC's.
    private void ShowClassLesson()
    {
        var lesson = _classRole.ShownLesson;
        ClassFolderButton.Caption = lesson.IsOn ? lesson.Turma : string.Empty;
    }

    // After every change to _tiles.
    private void PublishRoster() => Volatile.Write(ref _roster, _tiles.Select(tile => tile.Machine).ToList());

    // A machine answering: until the room has been asked, it is asked again soon (ClassFolderRole).
    private void PublishPresence(MachineTile tile)
    {
        var online = tile.Presence == TilePresence.Online;
        _online[tile.Machine.SessionThumbprint] = online;
        if (online)
        {
            _classRole.MachineAnswered();
        }
    }

    // For the whole room, whichever machines are in it, like "Agendar". The
    // window works what ClassFolderRole says now; a remote controller asks the
    // home for its state only while a window shows it.
    private void OnClassFolderClicked(object sender, RoutedEventArgs e) =>
        ShowOnce("pasta-da-turma", Array.Empty<KnownMachine>(), () =>
        {
            var controller = _classRole.Controller;
            var window = new ClassFolderWindow(controller, _classHandOver, _classAsideNote) { Owner = this };
            (controller as RemoteClassFolderController)?.Start();
            _classWindow = window;
            window.Closed += (_, _) => OnClassFolderWindowClosed(window);
            // Plan decision 8: the room is asked again as the window opens.
            _classRole.RefreshSoon();
            return window;
        });

    // Closed: no remote controller polls for it, and those given up meanwhile go (Task 5 note: only now).
    private void OnClassFolderWindowClosed(ClassFolderWindow window)
    {
        (window.Controller as RemoteClassFolderController)?.Stop();
        (_classRole.Controller as RemoteClassFolderController)?.Stop();
        if (ReferenceEquals(_classWindow, window))
        {
            _classWindow = null;
        }
        _classRole.ReleaseRetired();
    }

    // Every change of ClassFolderRole: the lesson under the button, and the
    // window, when open, given what to work now (it takes it once nothing it
    // started is running); the remote controller it gives up stops asking.
    private void OnClassRoleChanged()
    {
        ShowClassLesson();
        _ = ReadAsideNoteAsync();
        if (_classWindow is not { } window)
        {
            _classRole.ReleaseRetired();
            return;
        }
        var controller = _classRole.Controller;
        if (!ReferenceEquals(window.Controller, controller))
        {
            (window.Controller as RemoteClassFolderController)?.Stop();
            (controller as RemoteClassFolderController)?.Start();
        }
        // Even the one it has: one given it meanwhile and not taken yet is then not taken.
        window.Rebind(controller);
    }

    // The folders beside the class folders that a take-over left (ClassStore.AsideFolders),
    // and this PC's own classes while it works another PC's, in the window too when open.
    private async Task ReadAsideNoteAsync()
    {
        var remote = _classRole.Mode == ClassFolderMode.Remote;
        var where = Path.GetDirectoryName(ClassStore.DefaultFilesRoot) ?? string.Empty;
        _classAsideNote = await Task.Run(() => ClassFolderText.AsideNote(_classStore, remote, where));
        _classWindow?.ShowAsideNote(_classAsideNote);
    }
}
