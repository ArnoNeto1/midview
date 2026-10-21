using System.IO;
using System.Windows;
using System.Windows.Threading;
using Midview.Protocol;

namespace Midview.Master;

// One class in the window's list.
internal sealed record ClassRow(ClassSummary Class, string Summary)
{
    public string Turma => Class.Turma;
}

// One PC in the window's compact list during a lesson.
internal sealed record PcRow(string Text, string Hint, bool IsFailed, bool IsOffline);

// "Pasta da turma" (docs/superpowers/specs/2026-10-20-class-folder-design.md:
// "What the teacher sees (Master)"). Before a lesson: the classes, to create,
// rename, delete and start one. During a lesson: the class's files, each PC's
// state, and the end of the lesson with the teacher's choice for the files
// ("Trocar de turma…" ends it the same way, then shows the list).
//
// The store and the sync are MainWindow's, for its whole life: this window
// only shows them and asks them. Their events come on background threads (the
// sync's sometimes while it holds its lesson lock), so they are only ever
// passed on to the UI thread with BeginInvoke; and a start or an end runs off
// the UI thread, so the window never freezes.
//
// In three files: this one (the two views and what keeps them current),
// ClassFolderWindow.Classes.cs (the class list) and ClassFolderWindow.Lesson.cs
// (starting, ending, and what each PC answered).
public partial class ClassFolderWindow : Window
{
    // How often the views read the disk again (the files, the free space).
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly ClassStore _store;
    private readonly ClassFolderSync _sync;
    private readonly Func<IReadOnlyList<KnownMachine>> _machines;
    private readonly Func<KnownMachine, bool> _isOnline;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Action<ClassLesson> _onLessonChanged;
    private readonly Action<PcStatus> _onStatusChanged;

    // A PC's status was queued for the UI thread and not shown yet: the next one waits for it.
    private int _pcsQueued;

    // A start, an end or a change to the list is running: the buttons wait for it.
    private bool _busy;

    // Which view was shown last (null before the first): moving to the other clears the results.
    private bool? _shownLessonView;

    // The disk is being read for the views (RefreshFromDisk), and asked for again meanwhile.
    private bool _reading;
    private bool _readAgain;

    // The lesson's files as last read: for the header and the end choices' lines.
    private LessonFiles? _lessonFiles;

    // machines: the classroom list, as the class actions take it. isOnline: the
    // grid's view of each machine, for PCs the sync hasn't reached yet in this lesson.
    public ClassFolderWindow(
        ClassStore store, ClassFolderSync sync, Func<IReadOnlyList<KnownMachine>> machines, Func<KnownMachine, bool> isOnline)
    {
        InitializeComponent();
        _store = store;
        _sync = sync;
        _machines = machines;
        _isOnline = isOnline;
        NameTextBox.MaxLength = ClassLesson.MaxTurmaLength;

        _onLessonChanged = _ => Dispatcher.BeginInvoke(OnLessonChanged);
        _onStatusChanged = _ =>
        {
            if (Interlocked.Exchange(ref _pcsQueued, 1) == 0)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    Volatile.Write(ref _pcsQueued, 0);
                    ShowPcs();
                });
            }
        };
        _sync.LessonChanged += _onLessonChanged;
        _sync.StatusChanged += _onStatusChanged;

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();

        ShowView();
        RefreshFromDisk();
    }

    private ClassLesson Lesson => _sync.Lesson;

    // The lesson view while a lesson is on, the list otherwise.
    private bool InLessonView => Lesson.IsOn;

    // Started or ended, here or by the sync itself (a lesson left from an earlier day).
    private void OnLessonChanged()
    {
        // Every lesson's end starts as "Manter os arquivos".
        KeepChoice.IsChecked = true;
        _lessonFiles = null;
        ShowView();
        RefreshFromDisk();
    }

    // Which view, and everything in it that doesn't need the disk. Moving to
    // the other view clears the results: they were about the one left.
    private void ShowView()
    {
        var lesson = Lesson;
        var inLesson = InLessonView;
        if (_shownLessonView is { } shown && shown != inLesson)
        {
            ShowResults(string.Empty, Array.Empty<string>());
        }
        _shownLessonView = inLesson;
        ClassesView.Visibility = inLesson ? Visibility.Collapsed : Visibility.Visible;
        LessonView.Visibility = inLesson ? Visibility.Visible : Visibility.Collapsed;

        if (inLesson)
        {
            LessonTitleText.Text = $"Pasta da turma: {lesson.Turma}";
            DeleteAllLabel.Text = $"Apagar tudo do {lesson.Turma}";
            ShowLessonFiles();
            ShowPcs();
        }
        UpdateButtons();
    }

    // The header and the three choices' lines, from the files last read.
    private void ShowLessonFiles()
    {
        var lesson = Lesson;
        var files = _lessonFiles is { } read && string.Equals(read.Turma, lesson.Turma, StringComparison.OrdinalIgnoreCase)
            ? read
            : null;
        HeaderText.Text = files is null
            ? string.Empty
            : ClassFolderText.Header(lesson, files.Files, files.Bytes, files.FreeBytes);
        KeepHint.Text = ClassFolderText.EndChoiceHint(EndChoice.Keep, lesson.Turma, 0, 0, tooBigForTheBin: false);
        DeleteTodayHint.Text = files is null
            ? string.Empty
            : ClassFolderText.EndChoiceHint(EndChoice.DeleteToday, lesson.Turma, files.Today, files.Files, files.TooBigForTheBin(EndChoice.DeleteToday));
        DeleteAllHint.Text = files is null
            ? string.Empty
            : ClassFolderText.EndChoiceHint(EndChoice.DeleteAll, lesson.Turma, files.Today, files.Files, files.TooBigForTheBin(EndChoice.DeleteAll));
    }

    // Every PC of the classroom list: as the sync last saw it, or, before its
    // first round of this lesson, copying when it answers and off when not.
    private void ShowPcs()
    {
        if (!InLessonView)
        {
            return;
        }

        var statuses = _sync.Statuses.ToDictionary(status => status.Machine.SessionThumbprint, StringComparer.OrdinalIgnoreCase);
        PcsList.ItemsSource = _machines()
            .Select(machine => statuses.TryGetValue(machine.SessionThumbprint, out var status)
                ? status with { Machine = machine }
                : new PcStatus(machine, _isOnline(machine) ? PcSyncState.Copying : PcSyncState.Offline, string.Empty))
            .Select(status => new PcRow(
                ClassFolderText.State(status), ClassFolderText.StateHint(status),
                status.State == PcSyncState.Failed, status.State == PcSyncState.Offline))
            .ToList();
    }

    private void UpdateButtons()
    {
        var selected = SelectedClass;
        StartButton.Content = ClassFolderText.StartButton(selected?.Turma);
        StartButton.IsEnabled = !_busy && selected is not null;
        NewClassButton.IsEnabled = !_busy;
        NameOkButton.IsEnabled = !_busy;
        // Never the class whose lesson is on (the list only shows between lessons; this is the guard).
        RenameButton.IsEnabled = !_busy && selected is not null && !IsLessonClass(selected.Turma);
        DeleteButton.IsEnabled = !_busy && selected is not null && !IsLessonClass(selected.Turma);
        EndButton.IsEnabled = !_busy;
        SwitchButton.IsEnabled = !_busy;
        KeepChoice.IsEnabled = DeleteTodayChoice.IsEnabled = DeleteAllChoice.IsEnabled = !_busy;
    }

    // What the views need from the disk, read off the UI thread: the class
    // list, or the lesson's files; and the free space. One read at a time: a
    // call during one reads again after it.
    private async void RefreshFromDisk()
    {
        if (_reading)
        {
            _readAgain = true;
            return;
        }
        _reading = true;
        try
        {
            do
            {
                _readAgain = false;
                var lesson = Lesson;
                var inLesson = InLessonView;
                var read = await Task.Run(() => ReadDisk(inLesson, lesson.Turma));

                ShowLowSpace(read.FreeBytes);
                if (read.Classes is not null && !InLessonView)
                {
                    ShowClasses(read.Classes);
                }
                if (read.Files is not null && InLessonView && Lesson.LessonId == lesson.LessonId)
                {
                    _lessonFiles = read.Files with { FreeBytes = read.FreeBytes };
                    ShowLessonFiles();
                }
            }
            while (_readAgain);
        }
        finally
        {
            _reading = false;
        }
    }

    // The timer only reads during a lesson: the other classes' files don't change.
    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (InLessonView)
        {
            RefreshFromDisk();
        }
    }

    private DiskRead ReadDisk(bool inLesson, string turma) => inLesson
        ? new DiskRead(null, ReadLessonFiles(turma), _store.FreeBytes())
        : new DiskRead(ReadClasses(), null, _store.FreeBytes());

    private void ShowLowSpace(long freeBytes)
    {
        var warning = ClassFolderText.LowSpaceWarning(freeBytes);
        LowSpaceText.Text = warning ?? string.Empty;
        LowSpaceBorder.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // Null when the class's folder can't be read whole just now (the last numbers stay).
    private LessonFiles? ReadLessonFiles(string turma)
    {
        try
        {
            var files = _store.Files(turma);
            var today = DateOnly.FromDateTime(DateTime.Now);
            // A file the index hasn't seen yet arrived the day of its own time, as EndLesson counts it.
            var todays = files.Where(file => (_store.ArrivedOn(turma, file.Key)
                ?? DateOnly.FromDateTime(new DateTime(file.Value.LastWriteTicks, DateTimeKind.Utc).ToLocalTime())) == today).ToList();
            return new LessonFiles(
                turma, files.Count, files.Values.Sum(version => version.Size), todays.Count, todays.Sum(file => file.Value.Size),
                long.MaxValue, _store.RecycleBinBytes());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _sync.LessonChanged -= _onLessonChanged;
        _sync.StatusChanged -= _onStatusChanged;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    // The class's files as last read (all, and those that arrived today), the free
    // space on the disk that holds them, and what this PC's Recycle Bin holds there.
    private sealed record LessonFiles(
        string Turma, int Files, long Bytes, int Today, long TodayBytes, long FreeBytes, long BinBytes)
    {
        // Whether what that choice deletes is more than the Recycle Bin holds (it goes for good).
        public bool TooBigForTheBin(EndChoice choice) => (choice == EndChoice.DeleteAll ? Bytes : TodayBytes) > BinBytes;
    }

    // One read of the disk: the class list or the lesson's files (null when not read or not readable).
    private sealed record DiskRead(IReadOnlyList<ClassSummary>? Classes, LessonFiles? Files, long FreeBytes);
}
