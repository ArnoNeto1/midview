using System.Text.Json;
using Microsoft.Win32;

namespace Midview.Agent;

// The pin/unpin block of "Travar a barra de tarefas" (LabConfig.LockTaskbar)
// on whoever is logged on: with it, the student can neither pin a program to
// the taskbar nor unpin one already there (and dragging a shortcut onto it
// does nothing either). Kept on every poll of the Agent's loop
// (LabConfigCommandHandler.KeepTaskbarLock), so a student who logs on later
// is locked too, and applied right after the teacher turns it on or off.
//
// "Do not allow pinning programs to the Taskbar" (NoPinningToTaskbar) is a
// per-user policy, read only from the student's own hive
// (HKEY_USERS\<SID>\Software\Policies\Microsoft\Windows\Explorer): the
// machine-wide copy LabPolicyWriter writes in HKLM is ignored by Windows,
// which is why students could still pin and unpin with the lock on. A
// standard account can only read its Policies keys; the Agent, as SYSTEM,
// can write them. Explorer reads it when it starts, so it surely holds from
// the student's next logon; the value stays in their hive across logons.
//
// Only what Midview wrote is taken back: a value already 1 before the lock (a
// school's own policy) is left as it was. Which students it was written for
// is kept in a small record (StatePath), before it is written, so turning the
// lock off gives it back to each of them -- right away if they are logged on,
// or when they next log on (their hive is only there then).
//
// The logged-on student and their Software key are injected, so tests use a
// throwaway subtree per student and a temp record. Never throws: it runs in
// the Agent's loop; a failure is logged once, not on every poll.
public sealed class StudentTaskbarLock
{
    public static readonly string StatePath = Path.Combine(AgentDataDirectory.DirectoryPath, "taskbar-lock.json");

    internal const string ExplorerPath = @"Policies\Microsoft\Windows\Explorer";
    internal const string ValueName = "NoPinningToTaskbar";

    private readonly Func<string?> _loggedOnSid;
    private readonly Func<string, RegistryKey?> _openSoftware;
    private readonly string _statePath;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private string? _lastProblem;

    // loggedOnSid: the SID of the student logged on at the console, or null.
    // openSoftware: that student's Software key, writable, or null when their
    // hive isn't loaded; the caller here disposes it.
    public StudentTaskbarLock(
        Func<string?> loggedOnSid, Func<string, RegistryKey?> openSoftware, string statePath, Action<string> log)
    {
        _loggedOnSid = loggedOnSid;
        _openSoftware = openSoftware;
        _statePath = statePath;
        _log = log;
    }

    public static StudentTaskbarLock ForTheConsole() => new(
        () => UserSessionProcessLauncher.LoggedOnStudent()?.Student.Value,
        sid => Registry.Users.OpenSubKey($@"{sid}\Software", writable: true),
        StatePath, AgentLog.Warn);

    // The student logged on now made to match the lock: blocked from pinning
    // and unpinning while it is on, given back (what Midview wrote, and only
    // that) while it is off. Never throws.
    public void Keep(bool locked)
    {
        lock (_gate)
        {
            try
            {
                if (_loggedOnSid() is { } sid)
                {
                    var record = Load();
                    if (locked)
                    {
                        Lock(sid, record);
                    }
                    else if (record.Contains(sid))
                    {
                        GiveBack(sid, record);
                    }
                }
                _lastProblem = null;
            }
            catch (Exception ex)
            {
                if (ex.Message != _lastProblem)
                {
                    _lastProblem = ex.Message;
                    _log($"Taskbar lock (pin/unpin) for the logged-on student: {ex.Message}");
                }
            }
        }
    }

    private void Lock(string sid, HashSet<string> record)
    {
        using var software = _openSoftware(sid);
        if (software is null)
        {
            return;
        }
        using var key = software.CreateSubKey(ExplorerPath, writable: true);
        if (key.GetValue(ValueName) is int value && value == 1)
        {
            return; // already blocked: Midview's from earlier, or the school's own
        }
        // Recorded before it is written, so a crash in between still takes it back.
        if (record.Add(sid))
        {
            Save(record);
        }
        key.SetValue(ValueName, 1, RegistryValueKind.DWord);
    }

    private void GiveBack(string sid, HashSet<string> record)
    {
        using var software = _openSoftware(sid);
        if (software is null)
        {
            return; // not logged on after all: kept for when they are
        }
        using (var key = software.OpenSubKey(ExplorerPath, writable: true))
        {
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        record.Remove(sid);
        Save(record);
    }

    private HashSet<string> Load()
    {
        if (!File.Exists(_statePath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        var sids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_statePath)) ?? new List<string>();
        return new HashSet<string>(sids, StringComparer.OrdinalIgnoreCase);
    }

    private void Save(HashSet<string> record)
    {
        if (record.Count == 0)
        {
            File.Delete(_statePath);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var temp = _statePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(record.OrderBy(sid => sid, StringComparer.Ordinal).ToList()));
        File.Move(temp, _statePath, overwrite: true);
    }
}
