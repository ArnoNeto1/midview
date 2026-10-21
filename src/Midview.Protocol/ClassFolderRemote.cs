using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midview.Protocol;

// A colleague's PC working the home's "Pasta da turma" window ("Two teacher PCs
// in one lab", docs/superpowers/specs/2026-10-20-class-folder-design.md): the
// home's Master listens on Port, over TLS as for TeacherInvite, and the other
// Master asks for the window's state (RemoteState) and sends it the teacher's
// commands (RemoteCommand, answered with a RemoteReply). The messages are
// MessageType.RemoteClassFolderState and the ones after it, with JSON payloads
// (System.Text.Json, camelCase names). PCs are named by their computer name:
// the two Masters don't share a classroom list.
//
// Decoding refuses a payload over MaxJsonBytes and answers null for anything
// that isn't one of these: not JSON, a field of the wrong type, a field
// without a ? that is missing or null, a number below zero, an unknown Kind or
// Choice, a lesson that isn't one. Fields it doesn't know are ignored.
public static class ClassFolderRemote
{
    public const int Port = 28903;

    // The most a payload can be: the state is a few lines per class and PC.
    public const int MaxJsonBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static byte[] EncodeJson<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    // Null unless the payload is a valid T (one of the records below).
    public static T? DecodeJson<T>(byte[] payload) where T : class
    {
        if (payload.Length == 0 || payload.Length > MaxJsonBytes)
        {
            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(payload, Options);
            return value is not null && IsValid(value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsValid(object value) => value switch
    {
        RemoteState state => state.IsValid,
        RemoteCommand command => command.IsValid,
        RemoteReply reply => reply.IsValid,
        ClassLessonDto lesson => lesson.IsValid,
        ClassSummaryDto summary => summary.IsValid,
        LessonFilesDto files => files.IsValid,
        PcLineDto pc => pc.IsValid,
        ResultLineDto result => result.IsValid,
        _ => true,
    };
}

// The reply to RemoteClassFolderState: what the home's window shows now.
// HomeName is the home's computer name; Lesson the lesson it has (or none);
// Classes its list of classes; LessonFiles the numbers of the lesson's class
// (null when there is no lesson or they can't be read just now); Pcs each PC of
// its classroom; LastEndProblems what the last end of a lesson couldn't do;
// FreeBytes the free space on the disk that holds the folders (long.MaxValue
// when unknown); RecycleBinBytes what the home's Recycle Bin holds on that disk.
public sealed record RemoteState(
    [property: JsonRequired] string HomeName,
    [property: JsonRequired] ClassLessonDto Lesson,
    [property: JsonRequired] IReadOnlyList<ClassSummaryDto> Classes,
    LessonFilesDto? LessonFiles,
    [property: JsonRequired] IReadOnlyList<PcLineDto> Pcs,
    [property: JsonRequired] IReadOnlyList<string> LastEndProblems,
    [property: JsonRequired] long FreeBytes,
    [property: JsonRequired] long RecycleBinBytes)
{
    internal bool IsValid =>
        HomeName is { Length: <= ClassHome.MaxNameLength }
        && Lesson is { IsValid: true }
        && Classes is not null && Classes.All(summary => summary is { IsValid: true })
        && (LessonFiles is null || LessonFiles.IsValid)
        && Pcs is not null && Pcs.All(pc => pc is { IsValid: true })
        && LastEndProblems is not null && LastEndProblems.All(problem => problem is not null)
        && FreeBytes >= 0
        && RecycleBinBytes >= 0;
}

// The lesson as the window shows it: ClassLesson, field for field.
public sealed record ClassLessonDto(
    [property: JsonRequired] long SavedAt,
    [property: JsonRequired] Guid LessonId,
    [property: JsonRequired] string Turma,
    [property: JsonRequired] long StartedAt,
    [property: JsonRequired] bool Ended)
{
    public static ClassLessonDto From(ClassLesson lesson) =>
        new(lesson.SavedAt, lesson.LessonId, lesson.Turma, lesson.StartedAt, lesson.Ended);

    public ClassLesson ToLesson() => new(SavedAt, LessonId, Turma, StartedAt, Ended);

    internal bool IsValid => ToLesson().IsWellFormed;
}

// One class of the window's list: its files, their size, and the day the newest
// arrived (null when there are none).
public sealed record ClassSummaryDto(
    [property: JsonRequired] string Turma,
    [property: JsonRequired] int Files,
    [property: JsonRequired] long Bytes,
    DateOnly? LastLesson)
{
    internal bool IsValid => Turma is not null && Files >= 0 && Bytes >= 0;
}

// The numbers a lesson's window shows for its class: the files (count and
// bytes) and those that arrived today (count and bytes).
public sealed record LessonFilesDto(
    [property: JsonRequired] string Turma,
    [property: JsonRequired] int Files,
    [property: JsonRequired] long Bytes,
    [property: JsonRequired] int Today,
    [property: JsonRequired] long TodayBytes)
{
    internal bool IsValid => Turma is not null && Files >= 0 && Bytes >= 0 && Today >= 0 && TodayBytes >= 0;
}

// How one PC stands with the class folder (PcStatus): its computer name, its
// state (a PcSyncState's name) and what to tell the teacher about it.
public sealed record PcLineDto(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string State,
    [property: JsonRequired] string Detail)
{
    internal bool IsValid => Name is not null && State is not null && Detail is not null;
}

// What a command did on one PC.
public sealed record ResultLineDto(
    [property: JsonRequired] string Name,
    [property: JsonRequired] bool Ok,
    [property: JsonRequired] string Detail)
{
    internal bool IsValid => Name is not null && Detail is not null;
}

// RemoteClassFolderCommand's payload: what the teacher did in the colleague's
// window. Turma is the class it is about, NewName the name a rename gives it,
// Choice what happens to the files when a lesson ends. What each Kind needs
// of them is for the home to check: a command with too little is refused there,
// with a sentence the teacher can read.
public sealed record RemoteCommand(
    [property: JsonRequired] string Kind,
    string? Turma,
    string? NewName,
    string? Choice)
{
    public const string Start = "start";
    public const string End = "end";
    public const string Create = "create";
    public const string Rename = "rename";
    public const string Delete = "delete";

    // The choices at the end of a lesson (EndChoice.Keep, DeleteToday, DeleteAll).
    public const string Keep = "keep";
    public const string Today = "today";
    public const string All = "all";

    internal bool IsValid =>
        Kind is Start or End or Create or Rename or Delete
        && (Choice is null || Choice is Keep or Today or All);
}

// RemoteClassFolderCommandReply's payload: whether it worked (Ok), a sentence
// for the teacher (Detail, in Portuguese), what happened on each PC (Lines,
// when the command went to them) and the class it is about (Turma, when that
// applies).
public sealed record RemoteReply(
    [property: JsonRequired] bool Ok,
    [property: JsonRequired] string Detail,
    [property: JsonRequired] IReadOnlyList<ResultLineDto> Lines,
    string? Turma)
{
    internal bool IsValid =>
        Detail is not null && Lines is not null && Lines.All(line => line is { IsValid: true });
}
