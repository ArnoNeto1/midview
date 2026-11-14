using System.IO;
using System.Text.Json;

namespace Midview.Master;

// The apps the cable turned off on a device (PhoneCable.LocalAccounts) and
// has not turned back on yet, by the device's serial. Written before they are
// turned off and emptied once they are on again, so a cable that dropped in
// between gets them back on at the next Check of that device. Never throws:
// a file that can't be read or written is as if empty.
public sealed class CableReenableFile
{
    private readonly string _path;

    public CableReenableFile(string folder)
    {
        _path = Path.Combine(folder, "cable-reenable.json");
    }

    public static CableReenableFile ForThisComputer() => new(MasterData.Folder);

    public IReadOnlyList<string> For(string serial) =>
        Read().TryGetValue(serial, out var packages) ? packages : Array.Empty<string>();

    // Empty: nothing left to turn on for that device.
    public void Set(string serial, IReadOnlyList<string> packages)
    {
        var all = Read();
        if (packages.Count == 0)
        {
            if (!all.Remove(serial))
            {
                return;
            }
        }
        else
        {
            all[serial] = packages.ToList();
        }
        try
        {
            if (all.Count == 0)
            {
                File.Delete(_path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(all));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not remembered: only a dropped cable would need it.
        }
    }

    private Dictionary<string, List<string>> Read()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_path))
                ?? new Dictionary<string, List<string>>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, List<string>>();
        }
    }
}
