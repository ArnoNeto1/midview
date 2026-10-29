namespace Midview.Protocol;

// What the student's machine does once a file from the teacher is saved.
public enum AfterSend : byte
{
    Nothing = 0,

    // The "Arquivos do professor" folder, in File Explorer.
    OpenFolder = 1,

    // The file, opened or executed on the student's machine.
    OpenFile = 2,
}

// Chosen by the teacher in "Enviar arquivo". Overwrite replaces
// a file of the same name instead of saving "nome (2)".
public sealed record SendFileOptions(bool Overwrite, AfterSend AfterSend)
{
    public static readonly SendFileOptions Default = new(false, AfterSend.Nothing);
}

// Which files "Abrir ou executar o arquivo" may open: any file sent by the
// teacher, including scripts (.bat, .cmd) and programs (.exe).
public static class FileOpenPolicy
{
    public static bool MayOpen(string fileName) => !string.IsNullOrWhiteSpace(fileName);
}
