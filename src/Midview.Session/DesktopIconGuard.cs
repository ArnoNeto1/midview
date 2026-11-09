using System.Text.Json;

namespace Midview.Session;

// The icon half of "Travar a Área de Trabalho" (LabConfig.LockDesktop): while
// it is on, each Desktop icon stays where it was, whatever the student drags,
// sorts or arranges -- within a second it is put back. Windows has no setting
// for this; the file half (nothing created, deleted or renamed on the Desktop)
// is the Agent's, with NTFS permissions (StudentDesktopLock).
//
// The layout kept is the one on screen when the lock is turned on, saved in
// the student's own LocalAppData so it holds across logons. At a logon with
// the lock on and nothing saved yet (a new account, or the lock turned on while
// nobody was logged on), the layout is taken once Explorer has stopped placing
// icons: the same for SettlePasses passes in a row. An icon that appears later
// (the class folder when a lesson starts, "Arquivos do professor") is kept
// where Windows puts it; one that goes away is simply no longer there. What
// is inside the folders is not touched by any of this.
//
// "Organizar ícones automaticamente" is turned off while locked, since with it
// on Windows places every icon itself; the icons stay where they are when it
// goes off. Never throws: it runs on its own thread for as long as Session.
public sealed class DesktopIconGuard
{
    public const int SettlePasses = 3;

    private readonly IDesktopIcons _icons;
    private readonly Func<bool> _isLocked;
    private readonly string _layoutPath;
    private readonly Action<string> _log;
    private Dictionary<string, IconPoint>? _layout;
    private Dictionary<string, IconPoint>? _candidate;
    private int _stablePasses;
    private bool _firstPass = true;
    private bool _wasLocked;
    private string? _lastProblem;

    // isLocked: whether the lock is on now (LabDesktopLock.IsOn). layoutPath:
    // where the kept layout is saved, in the student's own folders.
    public DesktopIconGuard(IDesktopIcons icons, Func<bool> isLocked, string layoutPath, Action<string>? log = null)
    {
        _icons = icons;
        _isLocked = isLocked;
        _layoutPath = layoutPath;
        _log = log ?? Console.WriteLine;
    }

    // One look at the Desktop: what was moved goes back. Never throws.
    public void Pass()
    {
        try
        {
            Follow();
            _lastProblem = null;
        }
        catch (Exception ex)
        {
            if (ex.Message != _lastProblem)
            {
                _lastProblem = ex.Message;
                _log($"Desktop icon lock: {ex.Message}");
            }
        }
    }

    private void Follow()
    {
        var firstPass = _firstPass;
        _firstPass = false;
        if (!_isLocked())
        {
            if ((_wasLocked || firstPass) && File.Exists(_layoutPath))
            {
                File.Delete(_layoutPath); // nothing to keep once the teacher lets go
            }
            _wasLocked = false;
            _layout = null;
            _candidate = null;
            return;
        }

        if (!_wasLocked)
        {
            _wasLocked = true;
            // Found on at logon: the layout saved under the lock, if any.
            // Turned on now: what is on screen, as soon as it can be read.
            _layout = firstPass ? Load() : null;
            _candidate = null;
            _stablePasses = 0;
        }

        _icons.TurnOffAutoArrange();
        if (_icons.Read() is not { } now)
        {
            return; // Explorer isn't there right now
        }

        if (_layout is null)
        {
            if (firstPass || _candidate is not null)
            {
                Settle(now);
            }
            else
            {
                Keep(now);
            }
            return;
        }

        var back = new Dictionary<string, IconPoint>(StringComparer.OrdinalIgnoreCase);
        var learned = false;
        foreach (var (name, where) in now)
        {
            if (!_layout.TryGetValue(name, out var kept))
            {
                _layout[name] = where; // new on the Desktop: stays where Windows put it
                learned = true;
            }
            else if (kept != where)
            {
                back[name] = kept;
            }
        }
        if (learned)
        {
            Save(_layout);
        }
        if (back.Count > 0)
        {
            _icons.Place(back);
        }
    }

    // At logon, Explorer is still placing the icons: the layout is taken once
    // it has been the same for SettlePasses passes in a row.
    private void Settle(IReadOnlyDictionary<string, IconPoint> now)
    {
        if (_candidate is not null && Same(_candidate, now))
        {
            if (++_stablePasses >= SettlePasses - 1)
            {
                Keep(now);
            }
            return;
        }
        _candidate = new Dictionary<string, IconPoint>(now, StringComparer.OrdinalIgnoreCase);
        _stablePasses = 0;
    }

    private void Keep(IReadOnlyDictionary<string, IconPoint> now)
    {
        _layout = new Dictionary<string, IconPoint>(now, StringComparer.OrdinalIgnoreCase);
        _candidate = null;
        Save(_layout);
    }

    private static bool Same(IReadOnlyDictionary<string, IconPoint> a, IReadOnlyDictionary<string, IconPoint> b) =>
        a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out var other) && other == entry.Value);

    private Dictionary<string, IconPoint>? Load()
    {
        try
        {
            if (!File.Exists(_layoutPath))
            {
                return null;
            }
            var stored = JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(_layoutPath));
            if (stored is null)
            {
                return null;
            }
            var layout = new Dictionary<string, IconPoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, xy) in stored)
            {
                if (xy is { Length: 2 })
                {
                    layout[name] = new IconPoint(xy[0], xy[1]);
                }
            }
            return layout;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // taken again from the screen
        }
    }

    private void Save(Dictionary<string, IconPoint> layout)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_layoutPath)!);
        var temp = _layoutPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(layout.ToDictionary(entry => entry.Key, entry => new[] { entry.Value.X, entry.Value.Y })));
        File.Move(temp, _layoutPath, overwrite: true);
    }
}

// Whether "Travar a Área de Trabalho" is on: the Agent's saved lab
// configuration, %ProgramData%\Midview\lab-config.json, which students can read.
public static class LabDesktopLock
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Midview", "lab-config.json");

    // False when the file is missing, unreadable or has no LockDesktop (an
    // Agent from before it). Never throws.
    public static bool IsOn(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = JsonDocument.Parse(stream);
            return json.RootElement.TryGetProperty("LockDesktop", out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
