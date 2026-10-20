namespace Midview.Protocol;

// The folders Midview keeps on the student's Desktop: Session uses them, and
// the Agent protects them so the student can't delete or rename them.
public static class StudentFolderNames
{
    // Where files from the teacher land.
    public const string Inbox = "Arquivos do professor";

    // What the teacher collects.
    public const string Delivery = "Entregar ao professor";

    // The class's own files for the lesson (ClassFolderSync), saved on the
    // teacher's PC as they change.
    public const string ClassFolder = "Pasta da turma";

    public static readonly IReadOnlyList<string> All = new[] { Inbox, Delivery, ClassFolder };
}
