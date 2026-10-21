using System.Security.Principal;
using Microsoft.Win32;

namespace Midview.Agent;

// Points Chrome's and Edge's download folder at the lesson's class folder
// while a lesson is on, with the browsers' own "DownloadDirectory" policy
// (mandatory: the student can't pick another folder in the browser's
// settings, and "ask where to save" stops mattering).
//
// It goes in the student's own registry hive (HKEY_USERS\<SID>\Software), not
// in HKLM: the folder is on that student's Desktop, whose path differs from
// one account to the next. The Policies keys there aren't writable by a
// standard user, so only the Agent, as SYSTEM, can set or remove it.
//
// The key is injected (the student's Software key; in tests a throwaway
// subtree), so tests never touch the real policy keys. Only the
// DownloadDirectory values are written or removed; anything else under those
// keys is left alone.
public static class DownloadFolderPolicy
{
    public const string ChromeKey = @"Policies\Google\Chrome";
    public const string EdgeKey = @"Policies\Microsoft\Edge";
    private const string ValueName = "DownloadDirectory";

    private static readonly string[] Browsers = { ChromeKey, EdgeKey };

    public static void Apply(RegistryKey userSoftware, string folder)
    {
        foreach (var path in Browsers)
        {
            using var key = userSoftware.CreateSubKey(path, writable: true);
            key.SetValue(ValueName, folder, RegistryValueKind.String);
        }
    }

    public static void Remove(RegistryKey userSoftware)
    {
        foreach (var path in Browsers)
        {
            using var key = userSoftware.OpenSubKey(path, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    // What Chrome is pointed at now, or null: Chrome's value stands for both,
    // as Apply writes them together.
    public static string? Current(RegistryKey userSoftware)
    {
        using var key = userSoftware.OpenSubKey(ChromeKey);
        return key?.GetValue(ValueName) as string;
    }

    // The student's Software key, writable, or null when their hive isn't
    // loaded (they aren't logged on). The caller disposes it.
    public static RegistryKey? OpenUserSoftware(SecurityIdentifier student) =>
        Registry.Users.OpenSubKey($@"{student.Value}\Software", writable: true);
}
