using System.ComponentModel;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// MainWindow: which teacher PC keeps the class folders ("Two teacher PCs in
// one lab", docs/superpowers/specs/2026-10-20-class-folder-design.md). One
// ClassFolderRole for the window's whole life, over this PC's store and sync:
// it asks the room's Agents (ClassHomeTracker), serves this PC's folders to a
// colleague's teacher PC while this PC keeps them (ClassFolderHost, trusting
// the colleagues the Agents trust: ColleagueTrust), and otherwise has the
// window work the home's (RemoteClassFolderController). The pollers hand the
// lesson to the Agents, and take back an Agent that keeps an older home, only
// as the role says. One ClassFolderHandOver takes the folders over from the
// home, or gives them to a colleague's PC, and the Master doesn't close while
// one runs. With a single teacher PC, nothing of this shows: this PC is the
// home from its first lesson, its host open and unused (the setup opens its
// firewall port; the Master never does).
public partial class MainWindow
{
    private LocalClassFolderController _classLocal = null!;
    private ClassFolderHandOver _classHandOver = null!;
    private ClassFolderRole _classRole = null!;

    // After StartClassFolder, before the tiles: their pollers ask the role.
    private void StartClassHome()
    {
        var machineName = Environment.MachineName;
        IReadOnlyList<KnownMachine> Machines() => Volatile.Read(ref _roster);
        _classLocal = new LocalClassFolderController(_classStore, _classSync, Machines, IsOnline);
        _classHandOver = new ClassFolderHandOver(_classStore, _classSync, _masterCertificate, machineName, Machines, IsOnline);
        var trust = new ColleagueTrust(_masterCertificate, Machines, IsOnline);
        var tracker = new ClassHomeTracker(_masterCertificate, machineName, Machines, IsOnline);
        _classRole = new ClassFolderRole(
            tracker, _classSync, _classLocal, _classHandOver,
            () => ClassFolderHost.Open(_masterCertificate, _classLocal, trust.IsTrustedAsync, handOver: _classHandOver),
            () => new RemoteClassFolderController(_masterCertificate, () => tracker.Home, Machines, IsOnline),
            AgentsLesson);
        // Raised on the role's own thread: only queued here.
        _classRole.Changed += () => Dispatcher.BeginInvoke(OnClassRoleChanged);
        // A take-over over may have put classes aside here (on the hand-over's threads).
        _classHandOver.Changed += () => Dispatcher.BeginInvoke(() => _ = ReadAsideNoteAsync());
        if (Application.Current is { } app)
        {
            app.SessionEnding += (_, _) => _sessionEnding = true;
        }
        _classRole.Start();
        _ = ReadAsideNoteAsync();
    }

    // Windows signs out or shuts down: the Master closes, take-over or not.
    private bool _sessionEnding;

    // What the pollers hand each machine's Agent (on their threads).
    private ClassLesson? LessonForPollers() => _classRole.LessonForPollers;

    // The end the pollers hand an Agent still in a lesson this PC knows is over (on their threads).
    private ClassLesson? EndOfLessonOver(ClassLesson followed, ClassHome? keptHome) => _classRole.EndOfLessonOver(followed, keptHome);

    // Whether a lesson the pollers hand out is a held lesson's end, only for the Agents still in it (on their threads).
    private bool IsHeldEnd(ClassLesson lesson) => _classRole.IsHeldEnd(lesson);

    // While this PC keeps the class folders: its claim on each Agent, once per
    // connection, taking back one that keeps an older home and refreshing
    // where the others find this PC (on the pollers' threads), as its start
    // claims (ClassFolderSync).
    private ClassHomeClaim? StragglerClaim() => _classRole.HomeGeneration is { } generation
        ? new ClassHomeClaim(Environment.MachineName, ClassFolderRemote.Port, TakeOver: true, generation)
        : null;

    // The lesson the room's online Agents follow: the home's, for the button's label on another teacher PC.
    private ClassLesson? AgentsLesson() => ClassLessonOnAgents.NewestOf(
        Volatile.Read(ref _roster).Where(machine => machine.AgentThumbprint is { Length: > 0 } && IsOnline(machine)),
        _masterCertificate, ConnectionClient.AgentPort,
        message => System.Diagnostics.Trace.WriteLine("MainWindow: " + message),
        DateTimeOffset.Now.ToUnixTimeSeconds());

    // A take-over goes on for minutes after its copy is in place, on both
    // teacher PCs: closing now would leave it half done, so the Master stays
    // open until it ends (before the copy is in place, "Cancelar" in the
    // window stops it). A copy this PC sent that only waits for the PC that
    // took it to confirm runs nothing here: the teacher may close anyway. Not
    // when Windows signs out or shuts down: a message here would only hold
    // that up, and a take-over cut off is one the PCs already settle (Rulings
    // 17 to 21).
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_sessionEnding || Dispatcher.HasShutdownStarted)
        {
            return;
        }
        if (_classHandOver.IsRunning)
        {
            e.Cancel = true;
            MessageBox.Show(this, ClassFolderText.HandOverStillRunning, "Midview", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else if (_classHandOver.Unconfirmed is { Count: > 0 } pcs)
        {
            e.Cancel = MessageBox.Show(
                this, ClassFolderText.HandOverUnconfirmed(pcs), "Midview", MessageBoxButton.YesNo, MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes;
        }
    }
}
