namespace Midview.Protocol;

// What the student's machine does once a file from the teacher is saved.
public enum AfterSend : byte
{
    Nothing = 0,

    // The "Arquivos do professor" folder, in File Explorer.
    OpenFolder = 1,

    // The file, with the program Windows opens it with -- never a program
    // itself (FileOpenPolicy).
    OpenFile = 2,
}

// Chosen by the teacher in "Enviar arquivo". Overwrite replaces
// a file of the same name instead of saving "nome (2)".
public sealed record SendFileOptions(bool Overwrite, AfterSend AfterSend)
{
    public static readonly SendFileOptions Default = new(false, AfterSend.Nothing);
}

// Which files "Abrir o arquivo" may open: anything but what would run as a
// program, so a wrong click can't run something on the whole class at once.
public static class FileOpenPolicy
{
    private static readonly HashSet<string> Programs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".cpl", ".msi", ".msp", ".msc",
        ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta",
        ".lnk", ".url", ".reg", ".inf", ".jar", ".appref-ms", ".application",
    };

    public static bool MayOpen(string fileName) => !Programs.Contains(Path.GetExtension(fileName));
}
