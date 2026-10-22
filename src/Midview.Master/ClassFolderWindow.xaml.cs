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
// What it shows and asks goes through its controller (IClassFolderController):
// on the class folders' home PC, MainWindow's LocalClassFolderController over
// its store and sync, for its whole life. The controller's Changed comes on
// background threads (sometimes while the sync holds its lesson lock), so it is
// only ever passed on to the UI thread with BeginInvoke; and every read and
// command runs off the UI thread, so the window never freezes.
//
// In three files: this one (the two views and what keeps them current),
// ClassFolderWindow.Classes.cs (the class list) and ClassFolderWindow.Lesson.cs
// (starting, ending, and what each PC answered).
public partial class ClassFolderWindow : Window
{
    // How often the views read the disk again (the files, the free space).
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly IClassFolderController _controller;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Action _onChanged;

    // A change was queued for the UI thread and not shown yet: the next one waits for it.
    private int _changeQueued;

    // The lesson as OnLessonChanged last saw it: a change to it is a lesson
    // started or ended; any other change is a PC's state.
    private ClassLesson _shownLesson;

    // A start, an end or a change to the list is running: the buttons wait for it.
    private bool _busy;

    // Which view was shown last (null before the first): moving to the other clears the results.
    private bool? _shownLessonView;

    // The disk is being read for the views (RefreshFromDisk), and asked for again meanwhile.
    private bool _reading;
    private bool _readAgain;

    // The lesson's files as last read: for the header and the end choices' lines.
    private LessonFiles? _lessonFiles;

    // What the Recycle Bin holds on the disk of the classes, as last read: for "Excluir".
    private long _binBytes;

    public ClassFolderWindow(IClassFolderController controller)
    {
        InitializeComponent();
        _controller = controller;
        NameTextBox.MaxLength = ClassLesson.MaxTurmaLength;

        _shownLesson = _controller.Lesson;
        _onChanged = () =>
        {
            if (Interlocked.Exchange(ref _changeQueued, 1) == 0)
            {
                Dispatcher.BeginInvoke(OnChanged);
            }
        };
        _controller.Changed += _onChanged;

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();

        ShowView();
        RefreshFromDisk();
    }

    private ClassLesson Lesson => _controller.Lesson;

    // The lesson view while a lesson is on, the list otherwise.
    private bool InLessonView => Lesson.IsOn;

    // The lesson, or the PCs' states.
    private void OnChanged()
    {
        Volatile.Write(ref _changeQueued, 0);
        if (Lesson != _shownLesson)
        {
            OnLessonChanged();
        }
        else
        {
            ShowPcs();
        }
    }

    // Started or ended, here or by the sync itself (a lesson left from an earlier day).
    private void OnLessonChanged()
    {
        _shownLesson = Lesson;
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

    // Every PC of the classroom list, as the controller says each stands.
    private void ShowPcs()
    {
        if (!InLessonView)
        {
            return;
        }

        PcsList.ItemsSource = _controller.Pcs
            .Select(StatusOf)
            .Select(status => new PcRow(
                ClassFolderText.State(status), ClassFolderText.StateHint(status),
                status.State == PcSyncState.Failed, status.State == PcSyncState.Offline))
            .ToList();
    }

    // A PC's line as the texts take it (they only use its name); a state this
    // Midview doesn't know shows as a failure, with its detail.
    private static PcStatus StatusOf(PcLineDto line) => new(
        new KnownMachine(line.Name, string.Empty, string.Empty, null),
        Enum.TryParse<PcSyncState>(line.State, out var state) && Enum.IsDefined(state) ? state : PcSyncState.Failed,
        line.Detail);

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
                if (await ReadAsync(inLesson) is not { } read)
                {
                    continue; // not readable just now: the views keep what they show
                }

                ShowLowSpace(read.FreeBytes);
                _binBytes = read.RecycleBinBytes;
                if (!inLesson && !InLessonView)
                {
                    ShowClasses(read.Classes
                        .Select(summary => new ClassSummary(summary.Turma, summary.Files, summary.Bytes, summary.LastLesson))
                        .ToList());
                }
                if (inLesson && LessonFilesOf(read) is { } files && InLessonView && Lesson.LessonId == lesson.LessonId)
                {
                    _lessonFiles = files;
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

    // Null when it can't be read just now, whatever kept it from being read.
    private async Task<RemoteState?> ReadAsync(bool lessonFiles)
    {
        try
        {
            return await Task.Run(() => _controller.ReadAsync(lessonFiles));
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The lesson's numbers in a read; null when it has none (no lesson, or
    // its class's folder not readable whole just now: the last numbers stay).
    private static LessonFiles? LessonFilesOf(RemoteState read) => read.LessonFiles is { } files
        ? new LessonFiles(files.Turma, files.Files, files.Bytes, files.Today, files.TodayBytes, read.FreeBytes, read.RecycleBinBytes)
        : null;

    private void ShowLowSpace(long freeBytes)
    {
        var warning = ClassFolderText.LowSpaceWarning(freeBytes);
        LowSpaceText.Text = warning ?? string.Empty;
        LowSpaceBorder.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _controller.Changed -= _onChanged;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    // The class's files as last read (all, and those that arrived today), the free
    // space on the disk that holds them, and what the Recycle Bin holds there.
    private sealed record LessonFiles(
        string Turma, int Files, long Bytes, int Today, long TodayBytes, long FreeBytes, long BinBytes)
    {
        // Whether what that choice deletes is more than the Recycle Bin holds (it goes for good).
        public bool TooBigForTheBin(EndChoice choice) => (choice == EndChoice.DeleteAll ? Bytes : TodayBytes) > BinBytes;
    }
}
