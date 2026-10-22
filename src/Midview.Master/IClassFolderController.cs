using Midview.Protocol;

namespace Midview.Master;

// What the "Pasta da turma" window (ClassFolderWindow) works: the class
// folders' home PC's classes, lesson and PCs. On the home PC that is this PC's
// own store and sync (LocalClassFolderController), which ClassFolderHost also
// serves to a colleague's PC; on a colleague's PC, the home's, over the network
// ("Two teacher PCs in one lab", docs/superpowers/specs/2026-10-20-class-folder-design.md).
// PCs are named by their computer name, as the home's classroom list has them.
//
// Changed is raised on background threads, sometimes while the home's sync
// holds its lesson lock: a handler only queues its work (the window uses
// BeginInvoke). The window calls the rest off its UI thread.
public interface IClassFolderController
{
    // The lesson on now, or the last one, ended (ClassLesson.None(0) before any).
    ClassLesson Lesson { get; }

    // Every PC of the home's classroom list, in its order: the state is a
    // PcSyncState's name (read with Enum.TryParse and Enum.IsDefined).
    IReadOnlyList<PcLineDto> Pcs { get; }

    // What the last end of a lesson couldn't do with the class's files (one line each).
    IReadOnlyList<string> LastEndProblems { get; }

    // The home PC's computer name; null when this PC is the home.
    string? HomeName { get; }

    // The home PC's Midview doesn't answer (only ever true on a colleague's PC).
    bool HomeClosed { get; }

    // The lesson, a PC's state or the home itself changed.
    event Action? Changed;

    // What the window shows that comes from the disk, with the lesson, the PCs
    // and LastEndProblems. lessonFiles, with a lesson on: the numbers of its
    // class (LessonFiles, null when they can't be read just now) instead of the
    // class list (Classes, empty then): the window shows one or the other, and
    // reading the list goes through every class's folder. With no lesson on (one
    // that ended meanwhile, say), or lessonFiles false: the class list, and no
    // LessonFiles. Throws (IOException, say) when what it reads can't be read
    // just now: the window keeps what it shows.
    Task<RemoteState> ReadAsync(bool lessonFiles);

    // ClassStore.Create's answer: on success Turma is the name the folder got.
    Task<(bool Ok, string Detail, string Turma)> CreateAsync(string name);

    // Never the class whose lesson is on (refused, with Detail saying so).
    Task<(bool Ok, string Detail)> RenameAsync(string turma, string newName);

    Task<(bool Ok, string Detail)> DeleteAsync(string turma);

    // What each PC answered. A start another teacher PC keeps the class folders
    // for doesn't happen: the lines are the refusals and Lesson stays as it was
    // (the caller looks at it). Throws ClassFolderRefusedException with a lesson
    // on or a class that is gone.
    Task<IReadOnlyList<ResultLineDto>> StartAsync(string turma);

    // Ends the lesson of that class, as chosen, with what each PC answered.
    // Throws ClassFolderRefusedException when that lesson isn't on (any more):
    // a window that still showed it must not end the next one.
    Task<IReadOnlyList<ResultLineDto>> EndAsync(string turma, EndChoice choice);
}

// A command the home refused as a whole, with why in the teacher's words (the
// window shows Message as it is).
public sealed class ClassFolderRefusedException : Exception
{
    public ClassFolderRefusedException(string message)
        : base(message)
    {
    }
}
