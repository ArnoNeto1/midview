using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Midview.Master;

// How much the teacher's Recycle Bin holds on the drive of a folder: files
// that come to more than that don't go to it, Windows deletes them for good
// (WindowsRecycleBin asks nothing), so the "Pasta da turma" window must not
// promise the Lixeira for them. The Recycle Bin's own settings say it (per
// user and drive: HKCU ...\Explorer\BitBucket\Volume\{volume}, MaxCapacity in
// MB, NukeOnDelete); when they can't be read, a tenth of the drive is taken.
public static class RecycleBinRoom
{
    private const string Settings = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume";

    // The bytes the Recycle Bin of folder's drive holds; 0 where there is none
    // (a network folder, a drive that can't be read, a bin that deletes for good).
    public static long CapacityFor(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return 0;
            }

            var drive = new DriveInfo(root);
            if (drive.DriveType is DriveType.Network or DriveType.CDRom)
            {
                return 0;
            }

            var (megabytes, deletesForGood) = BinSettings(root);
            return Capacity(megabytes, deletesForGood, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or System.Security.SecurityException)
        {
            return 0;
        }
    }

    // What the settings say: configuredMegabytes (null when not set) unless the
    // bin deletes for good; a tenth of the drive when not set.
    public static long Capacity(long? configuredMegabytes, bool deletesForGood, long driveBytes) =>
        deletesForGood ? 0
        : configuredMegabytes is { } megabytes ? megabytes * 1024 * 1024
        : driveBytes / 10;

    private static (long? Megabytes, bool DeletesForGood) BinSettings(string root)
    {
        var volume = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPoint(root, volume, (uint)volume.Capacity))
        {
            return (null, false);
        }

        // "\\?\Volume{GUID}\": the key is named by the GUID, braces included.
        var name = volume.ToString();
        var open = name.IndexOf('{');
        var close = name.IndexOf('}');
        if (open < 0 || close < open)
        {
            return (null, false);
        }

        using var key = Registry.CurrentUser.OpenSubKey($@"{Settings}\{name[open..(close + 1)]}");
        var megabytes = key?.GetValue("MaxCapacity") is int capacity && capacity >= 0 ? capacity : (long?)null;
        var deletesForGood = key?.GetValue("NukeOnDelete") is int nuke && nuke != 0;
        return (megabytes, deletesForGood);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeNameForVolumeMountPointW")]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, StringBuilder volumeName, uint length);
}
