using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// The "Pasta da turma" window's controller on the class folders' home PC: this
// PC's store (ClassStore) and sync (ClassFolderSync), as the window used them
// before a colleague's PC could work it too. One for the Master's whole life,
// like the store and the sync: the window and ClassFolderHost (the colleague's
// commands) share it, and its commands run one at a time, so both windows end
// in the same state. That is also why it guards what one window may ask of a
// state the other already changed: no start over a lesson on, no end of a
// lesson that isn't on, no rename or deletion of the lesson's class.
//
// machines: the classroom list; isOnline: the grid's view of each machine (both
// called from background threads too). localNow: "today" for the files that
// arrived in this lesson (the store's own clock). homeName: this PC's computer
// name, which a colleague's window shows.
public sealed class LocalClassFolderController : IClassFolderController
{
    private readonly ClassStore _store;
    private readonly ClassFolderSync _sync;
    private readonly Func<IReadOnlyList<KnownMachine>> _machines;
    private readonly Func<KnownMachine, bool> _isOnline;
    private readonly Func<DateTime> _localNow;
    private readonly string _homeName;
    private readonly Action<ClassLesson> _onLessonChanged;
    private readonly Action<PcStatus> _onStatusChanged;

    // One command at a time, whichever window it came from.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // The sync's events are followed only while someone follows Changed, so a
    // window closed leaves nothing behind on the sync.
    private readonly object _listeners = new();
    private Action? _changed;

    public LocalClassFolderController(
        ClassStore store, ClassFolderSync sync, Func<IReadOnlyList<KnownMachine>> machines, Func<KnownMachine, bool> isOnline,
        Func<DateTime>? localNow = null, string? homeName = null)
    {
        _store = store;
        _sync = sync;
        _machines = machines;
        _isOnline = isOnline;
        _localNow = localNow ?? (() => DateTime.Now);
        _homeName = homeName ?? Environment.MachineName;
        _onLessonChanged = _ => Volatile.Read(ref _changed)?.Invoke();
        _onStatusChanged = _ => Volatile.Read(ref _changed)?.Invoke();
    }

    public ClassLesson Lesson => _sync.Lesson;

    // As the sync last saw each PC, or, before its first round of this lesson,
    // copying when it answers and off when not.
    public IReadOnlyList<PcLineDto> Pcs
    {
        get
        {
            var statuses = _sync.Statuses.ToDictionary(status => status.Machine.SessionThumbprint, StringComparer.OrdinalIgnoreCase);
            return _machines()
                .Select(machine => statuses.TryGetValue(machine.SessionThumbprint, out var status)
                    ? new PcLineDto(machine.Name, status.State.ToString(), status.Detail)
                    : new PcLineDto(machine.Name, (_isOnline(machine) ? PcSyncState.Copying : PcSyncState.Offline).ToString(), string.Empty))
                .ToList();
        }
    }

    public IReadOnlyList<string> LastEndProblems => _sync.LastEndProblems;

    public string? HomeName => null;

    public bool HomeClosed => false;

    public event Action? Changed
    {
        add
        {
            if (value is null)
            {
                return;
            }
            lock (_listeners)
            {
                if (_changed is null)
                {
                    _sync.LessonChanged += _onLessonChanged;
                    _sync.StatusChanged += _onStatusChanged;
                }
                _changed += value;
            }
        }
        remove
        {
            lock (_listeners)
            {
                if (_changed is null)
                {
                    return;
                }
                _changed -= value;
                if (_changed is null)
                {
                    _sync.LessonChanged -= _onLessonChanged;
                    _sync.StatusChanged -= _onStatusChanged;
                }
            }
        }
    }

    // The class list throws IOException or UnauthorizedAccessException when the
    // classes' folder can't be read just now. The lesson is read once, so the
    // numbers and the lesson in a state always go together.
    public Task<RemoteState> ReadAsync(bool lessonFiles) => Task.Run(() =>
    {
        var lesson = Lesson;
        IReadOnlyList<ClassSummaryDto> classes = Array.Empty<ClassSummaryDto>();
        LessonFilesDto? files = null;
        if (lessonFiles && lesson.IsOn)
        {
            files = ReadLessonFiles(lesson.Turma);
        }
        else
        {
            classes = _store.List()
                .Select(summary => new ClassSummaryDto(summary.Turma, summary.Files, summary.Bytes, summary.LastLesson))
                .ToList();
        }
        return new RemoteState(
            _homeName, ClassLessonDto.From(lesson), classes, files, Pcs, LastEndProblems, _store.FreeBytes(), _store.RecycleBinBytes());
    });

    public Task<(bool Ok, string Detail, string Turma)> CreateAsync(string name) =>
        OneAtATime(() => Task.Run(() => _store.Create(name)));

    // What isn't a class name (a path, say) is a class that isn't there: it never reaches the store's folders.
    public Task<(bool Ok, string Detail)> RenameAsync(string turma, string newName) =>
        OneAtATime(() => !ClassStore.IsValidName(turma) ? Task.FromResult((false, ClassFolderText.ClassGone))
            : IsLessonClass(turma) ? Task.FromResult((false, ClassFolderText.LessonClassNotRenamed))
            : Task.Run(() => _store.Rename(turma, newName)));

    public Task<(bool Ok, string Detail)> DeleteAsync(string turma) =>
        OneAtATime(() => !ClassStore.IsValidName(turma) ? Task.FromResult((false, ClassFolderText.ClassGone))
            : IsLessonClass(turma) ? Task.FromResult((false, ClassFolderText.LessonClassNotDeleted))
            : Task.Run(() => _store.Delete(turma)));

    public Task<IReadOnlyList<ResultLineDto>> StartAsync(string turma) => OneAtATime(async () =>
    {
        if (Lesson is { IsOn: true } on)
        {
            throw new ClassFolderRefusedException(ClassFolderText.LessonAlreadyOn(on.Turma));
        }
        if (!await Task.Run(() => _store.Exists(turma)).ConfigureAwait(false))
        {
            throw new ClassFolderRefusedException(ClassFolderText.ClassGone);
        }
        return Lines(await _sync.StartAsync(turma).ConfigureAwait(false));
    });

    public Task<IReadOnlyList<ResultLineDto>> EndAsync(string turma, EndChoice choice) => OneAtATime(async () =>
    {
        if (!IsLessonClass(turma))
        {
            throw new ClassFolderRefusedException(ClassFolderText.LessonAlreadyOver(turma));
        }
        return Lines(await _sync.EndAsync(choice).ConfigureAwait(false));
    });

    private async Task<T> OneAtATime<T>(Func<Task<T>> command)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await command().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsLessonClass(string turma) =>
        Lesson is { IsOn: true } lesson && string.Equals(lesson.Turma, turma, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ResultLineDto> Lines(IReadOnlyList<(KnownMachine Machine, bool Ok, string Detail)> results) =>
        results.Select(result => new ResultLineDto(result.Machine.Name, result.Ok, result.Detail)).ToList();

    // Null when the class's folder can't be read whole just now (the window keeps the last numbers).
    private LessonFilesDto? ReadLessonFiles(string turma)
    {
        try
        {
            var files = _store.Files(turma);
            var today = DateOnly.FromDateTime(_localNow());
            // A file the index hasn't seen yet arrived the day of its own time, as EndLesson counts it.
            var todays = files.Where(file => (_store.ArrivedOn(turma, file.Key)
                ?? DateOnly.FromDateTime(new DateTime(file.Value.LastWriteTicks, DateTimeKind.Utc).ToLocalTime())) == today).ToList();
            return new LessonFilesDto(
                turma, files.Count, files.Values.Sum(version => version.Size), todays.Count, todays.Sum(file => file.Value.Size));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
