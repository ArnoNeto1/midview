using System.IO;
using System.Text.Json;

namespace Midview.Master;

// The teacher's own classroom roster -- a per-user convenience file, not a
// security boundary, so unlike trusted-masters.json it gets no special ACL.
public static class KnownMachinesFile
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Midview", "known-machines.json");

    public static IReadOnlyList<KnownMachine> Load(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<KnownMachine>();
        }

        var json = File.ReadAllText(path);
        try
        {
            return JsonSerializer.Deserialize<List<KnownMachine>>(json) ?? new List<KnownMachine>();
        }
        catch (JsonException ex)
        {
            // Moved aside instead of left in place: the teacher's next save
            // would otherwise silently overwrite whatever it still held.
            var corruptPath = path + ".corrupt";
            File.Move(path, corruptPath, overwrite: true);
            throw new InvalidDataException($"arquivo ilegível, movido para {corruptPath}", ex);
        }
    }

    public static void Save(string path, IReadOnlyList<KnownMachine> machines)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(machines, WriteOptions));
    }
}
