namespace Midview.Protocol;

// Words the common file and permission errors in Portuguese for the
// teacher's windows and the student's notices: .NET's own messages come in
// English whatever the Windows language (the first live run showed "Access
// to the path ... is denied" to the teacher). Errors Windows itself words,
// such as a Win32Exception's, already come in the system's language, and
// anything else keeps its own text.
public static class ErrorText
{
    private const int ErrorHandleDiskFull = 0x27;
    private const int ErrorSharingViolation = 0x20;
    private const int ErrorLockViolation = 0x21;
    private const int ErrorDiskFull = 0x70;

    public static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "acesso negado",
        DirectoryNotFoundException => "pasta não encontrada",
        FileNotFoundException => "arquivo não encontrado",
        DriveNotFoundException => "unidade não encontrada",
        PathTooLongException => "caminho longo demais",
        IOException io when Win32Code(io) is ErrorDiskFull or ErrorHandleDiskFull => "disco cheio",
        IOException io when Win32Code(io) is ErrorSharingViolation or ErrorLockViolation =>
            "o arquivo está em uso por outro programa",
        _ => ex.Message,
    };

    // Only for errors Windows itself raised (facility 7), whose low word is the Win32 code.
    private static int Win32Code(IOException io) =>
        (io.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) ? io.HResult & 0xFFFF : 0;
}
