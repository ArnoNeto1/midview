using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Midview.Session;

// The real shell32 calls behind IShellFolders. Nothing here is unit-tested for
// SetDownloads: it would move the developer's own Downloads folder.
public sealed class WindowsShellFolders : IShellFolders
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    // Without this flag the shell may rewrite the path into %USERPROFILE%\...
    // form; the class folder is stored as the full path it was given.
    private const uint DontUnexpand = 0x2000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHSetKnownFolderPath(
        ref Guid folderId, uint flags, IntPtr token, [MarshalAs(UnmanagedType.LPWStr)] string path);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr ILCreateFromPathW([MarshalAs(UnmanagedType.LPWStr)] string path);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern uint ILGetSize(IntPtr pidl);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void ILFree(IntPtr pidl);

    // The buffer must hold MAX_PATH characters; the function writes no more.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder path);

    private const int MaxPath = 260;

    public void SetDownloads(string path)
    {
        var id = DownloadsFolderId;
        var hr = SHSetKnownFolderPath(ref id, DontUnexpand, IntPtr.Zero, path);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }

    public byte[]? IdListOf(string folder)
    {
        var pidl = ILCreateFromPathW(folder);
        if (pidl == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // ILGetSize counts the terminating zero USHORT, which the saved list needs.
            var size = (int)ILGetSize(pidl);
            if (size < 2)
            {
                return null;
            }

            var bytes = new byte[size];
            Marshal.Copy(pidl, bytes, 0, size);
            return bytes;
        }
        finally
        {
            ILFree(pidl);
        }
    }

    public string? PathOf(byte[] idList)
    {
        if (!IsWholeIdList(idList))
        {
            return null;
        }

        // The shell reads native memory, so the bytes are pinned while it does.
        var handle = GCHandle.Alloc(idList, GCHandleType.Pinned);
        try
        {
            var path = new StringBuilder(MaxPath);
            return SHGetPathFromIDListW(handle.AddrOfPinnedObject(), path) ? path.ToString() : null;
        }
        finally
        {
            handle.Free();
        }
    }

    // The bytes come from the registry, where the student can write anything:
    // every item's size (a USHORT that counts itself) must stay inside them and a
    // zero size must end the list, so the shell never reads past the array.
    private static bool IsWholeIdList(byte[] idList)
    {
        var offset = 0;
        while (offset + 2 <= idList.Length)
        {
            var size = BinaryPrimitives.ReadUInt16LittleEndian(idList.AsSpan(offset));
            if (size == 0)
            {
                return true;
            }
            if (size < 2 || size > idList.Length - offset - 2)
            {
                return false;
            }
            offset += size;
        }
        return false;
    }
}
