using System.Text.Json;

namespace Midview.Agent;

// The programs a teacher asked to remove from this machine, kept for KeepFor
// so the "just for me" copies of the students who weren't logged on at the
// time go too -- a lab machine has many accounts, and the teacher's request
// only reaches the one at the console. Each of them is removed the next time
// its student logs on (PendingUninstallWorker), in that student's session:
// run as SYSTEM, a per-user uninstaller leaves the student's copy behind, so
// this waits for them rather than reaching into a logged-off profile. Asking
// again for the same program starts its days over.
//
// Machine-wide programs and Store packages are removed for every account when
// the teacher asks; their names are kept here too, harmlessly, since the logon
// pass only looks at the student's own programs.
public sealed class PendingUninstalls
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(14);

    public static string DefaultPath => Path.Combine(AgentDataDirectory.DirectoryPath, "pending-uninstalls.json");

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<string, DateTime> _until;

    // utcNow is for tests; the clock everywhere else.
    public PendingUninstalls(string path, Func<DateTime>? utcNow = null)
    {
        _path = path;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _until = Load(path);
    }

    public void Add(IEnumerable<string> displayNames)
    {
        lock (_gate)
        {
            var until = _utcNow() + KeepFor;
            foreach (var name in displayNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _until[name.Trim()] = until;
                }
            }
            DropExpired();
            TrySave();
        }
    }

    // The names still within their days, in order.
    public IReadOnlyList<string> Current()
    {
        lock (_gate)
        {
            if (DropExpired())
            {
                TrySave();
            }
            return _until.Keys.Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    private bool DropExpired()
    {
        var now = _utcNow();
        var expired = _until.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList();
        foreach (var name in expired)
        {
            _until.Remove(name);
        }
        return expired.Count > 0;
    }

    // Unreadable means nothing pending: the students' copies just stay until
    // the teacher asks again. A bad file is kept aside, as with the program
    // block, so it isn't read (and fails) at every start.
    private static Dictionary<string, DateTime> Load(string path)
    {
        var until = new Dictionary<string, DateTime>(StringComparer.CurrentCultureIgnoreCase);
        if (!File.Exists(path))
        {
            return until;
        }

        try
        {
            foreach (var stored in JsonSerializer.Deserialize<List<StoredName>>(File.ReadAllText(path)) ?? new List<StoredName>())
            {
                if (!string.IsNullOrWhiteSpace(stored.Name))
                {
                    until[stored.Name.Trim()] = DateTime.SpecifyKind(stored.UntilUtc, DateTimeKind.Utc);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            AgentLog.Warn($"Couldn't read the pending uninstalls, starting with none: {ex.Message}");
            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                // Left in place; it's read again (and fails again) next start.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Warn($"Couldn't read the pending uninstalls file, starting with none: {ex.Message}");
        }
        return until;
    }

    // A failure to save only costs the other accounts' copies after a
    // restart; it must never fail the teacher's request.
    private void TrySave()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _path + ".tmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, _until.Select(pair => new StoredName(pair.Key, pair.Value)).ToList());
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentLog.Warn($"Couldn't save the pending uninstalls: {ex.Message}");
        }
    }

    private sealed record StoredName(string Name, DateTime UntilUtc);
}
