using System.Text.Json;
using Microsoft.Win32;

namespace Midview.Agent;

// Empties the Ctrl+Alt+Del screen for the student logged on, while the screen
// is locked: the Task Manager (DisableTaskMgr), signing out (NoLogoff), locking
// the PC (DisableLockWorkstation, Win+L too), changing the password
// (DisableChangePassword) and the power button's shut down, restart and sleep
// (NoClose, the Start menu's too; the teacher's shutdown, run by the Agent, is
// not a user's and still works) are taken off it, so the blue screen still opens --
// nothing short of a keyboard driver stops that -- but only "Cancelar" leads
// anywhere. One of the policies SwitchUserBlockHandler holds, beside switch user.
//
// They are per-user policies, in the student's own hive
// (HKEY_USERS\<SID>\Software\Microsoft\Windows\CurrentVersion\Policies), whose
// Policies keys a standard account can only read: the Session, as the student,
// couldn't write them (and crashed trying to, until 0.0.43). The Agent, as
// SYSTEM, can.
//
// Only what Midview wrote is taken back: a value already 1 before the lock (a
// school's own policy) is left as it was. What was written is kept in a small
// record (StatePath), per student, before it is written, so a lock left by a
// crash or a power cut is taken back too -- on the next Agent's first pass if
// that student is logged on, or when they next log on (their hive is only
// there then). Keep, on every pass of the Agent's loop, also locks down whoever
// logs on during a lock.
//
// The logged-on student and their Software key are injected, so tests use a
// throwaway subtree per student and a temp record. Never throws: it runs in
// the Agent's loop and in the lock's hold, and logs what fails instead.
public sealed class StudentCtrlAltDelLockdown : ISwitchUserPolicy
{
    public static readonly string StatePath = Path.Combine(AgentDataDirectory.DirectoryPath, "ctrl-alt-del-lockdown.json");

    private static readonly (string Path, string Name)[] Values =
    {
        (@"Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr"),
        (@"Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoLogoff"),
        (@"Microsoft\Windows\CurrentVersion\Policies\System", "DisableLockWorkstation"),
        (@"Microsoft\Windows\CurrentVersion\Policies\System", "DisableChangePassword"),
        (@"Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoClose"),
    };

    private readonly Func<string?> _loggedOnSid;
    private readonly Func<string, RegistryKey?> _openSoftware;
    private readonly string _statePath;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private bool _locked;

    // loggedOnSid: the SID of the student logged on at the console, or null.
    // openSoftware: that student's Software key, writable, or null when their
    // hive isn't loaded; the caller here disposes it.
    public StudentCtrlAltDelLockdown(
        Func<string?> loggedOnSid, Func<string, RegistryKey?> openSoftware, string statePath, Action<string> log)
    {
        _loggedOnSid = loggedOnSid;
        _openSoftware = openSoftware;
        _statePath = statePath;
        _log = log;
    }

    public static StudentCtrlAltDelLockdown ForTheConsole() => new(
        () => UserSessionProcessLauncher.LoggedOnStudent()?.Student.Value,
        sid => Registry.Users.OpenSubKey($@"{sid}\Software", writable: true),
        StatePath, AgentLog.Warn);

    public void Block()
    {
        lock (_gate)
        {
            _locked = true;
            Keep();
        }
    }

    public void Unblock()
    {
        lock (_gate)
        {
            _locked = false;
            Keep();
        }
    }

    // The student logged on now made to match the lock: locked down while it
    // is on, given back (what Midview wrote, and only that) while it is off.
    public void Keep()
    {
        lock (_gate)
        {
            try
            {
                if (_loggedOnSid() is not { } sid)
                {
                    return;
                }
                var record = Load();
                if (_locked)
                {
                    LockDown(sid, record);
                }
                else if (record.ContainsKey(sid))
                {
                    GiveBack(sid, record);
                }
            }
            catch (Exception ex)
            {
                _log($"Ctrl+Alt+Del lockdown (Task Manager, sign out, lock, change password, power): {ex.Message}");
            }
        }
    }

    private void LockDown(string sid, Dictionary<string, List<string>> record)
    {
        using var software = _openSoftware(sid);
        if (software is null)
        {
            return;
        }
        var written = record.TryGetValue(sid, out var names) ? names : new List<string>();
        foreach (var (path, name) in Values)
        {
            using var key = software.CreateSubKey(path, writable: true);
            if (key.GetValue(name) is int value && value == 1)
            {
                continue; // already off: Midview's from earlier, or the school's own
            }
            // Recorded before it is written, so a crash in between still takes it back.
            if (!written.Contains(name))
            {
                written.Add(name);
                record[sid] = written;
                Save(record);
            }
            key.SetValue(name, 1, RegistryValueKind.DWord);
        }
    }

    private void GiveBack(string sid, Dictionary<string, List<string>> record)
    {
        using var software = _openSoftware(sid);
        if (software is null)
        {
            return; // not logged on after all: kept for when they are
        }
        foreach (var (path, name) in Values)
        {
            if (!record[sid].Contains(name))
            {
                continue;
            }
            using var key = software.OpenSubKey(path, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
        record.Remove(sid);
        Save(record);
    }

    private Dictionary<string, List<string>> Load()
    {
        if (!File.Exists(_statePath))
        {
            return new Dictionary<string, List<string>>();
        }
        return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_statePath))
            ?? new Dictionary<string, List<string>>();
    }

    private void Save(Dictionary<string, List<string>> record)
    {
        if (record.Count == 0)
        {
            File.Delete(_statePath);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var temp = _statePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(record));
        File.Move(temp, _statePath, overwrite: true);
    }
}
