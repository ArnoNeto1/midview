using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Puts a reparse point that is no link on an existing file: the kind a cloud
// files provider (OneDrive's Files On-Demand) sets on its placeholders. It
// has the ReparsePoint attribute, and no link target. The tag is one nobody
// handles, so the file can't be opened for its contents afterwards, but it
// can be listed, looked at and deleted.
//
// Also compiled into the Master and Session test projects (Compile Link).
public static class NonLinkReparsePoint
{
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;

    // A Microsoft-range tag (bit 31 set, bit 29, the name surrogate bit, clear) that is assigned to nobody:
    // no driver handles it, and a generic buffer (header and some data) is accepted for it. A third-party
    // tag needs a GUID buffer, and Windows refused every one tried.
    private const uint Tag = 0x8000AB12;

    public static void Make(string file)
    {
        using var handle = CreateFile(file, GenericWrite, 0, IntPtr.Zero, OpenExisting, FileFlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        // REPARSE_DATA_BUFFER: tag, data length, reserved, then the data (four bytes of nothing).
        var buffer = new byte[8 + 4];
        BitConverter.GetBytes(Tag).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)4).CopyTo(buffer, 4);
        if (!DeviceIoControl(handle, FsctlSetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint code, byte[] input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
}
