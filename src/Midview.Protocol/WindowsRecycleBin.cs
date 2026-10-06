using System.Runtime.InteropServices;

namespace Midview.Protocol;

// Where the "Apagar" buttons send files: the Recycle Bin of whoever runs
// the code (the student, for their delivery folder; the teacher, for what
// was collected), so a wrong click can still be undone.
public interface IRecycleBin
{
    // Throws if the file or folder couldn't be moved (in use, say).
    void Recycle(string path);
}

// Through the shell, as File Explorer's Delete does, with no dialogs: Session
// runs unattended on the student's screen. Where there's no Recycle Bin (a
// drive without one, or a file too big for it) Windows deletes for good.
public sealed class WindowsRecycleBin : IRecycleBin
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    public void Recycle(string path)
    {
        var operation = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path) + '\0', // the list ends with a second null
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        var result = SHFileOperation(ref operation);
        if (result != 0 || operation.fAnyOperationsAborted || File.Exists(path) || Directory.Exists(path))
        {
            // As a Win32 error, so ErrorText can word the usual ones (in use...).
            throw new IOException($"{Path.GetFileName(path)} não foi para a Lixeira (código {result})",
                unchecked((int)0x80070000) | (result & 0xFFFF));
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT operation);
}
