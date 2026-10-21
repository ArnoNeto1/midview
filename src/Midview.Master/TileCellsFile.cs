using System.IO;
using System.Text.Json;

namespace Midview.Master;

// Where the teacher placed each machine's tile ("Organizar sala"), by the
// machine's name, so the room looks the same the next time Midview opens.
// Kept apart from the classroom list: it is how this teacher sees the room,
// not part of the room a colleague is given.
public static class TileCellsFile
{
    public static string DefaultPath => Path.Combine(MasterData.Folder, "tile-cells.json");

    // Missing or unreadable is no places yet: the tiles start from the rows.
    public static Dictionary<string, TileCell> Load(string path)
    {
        try
        {
            var cells = JsonSerializer.Deserialize<Dictionary<string, TileCell>>(File.ReadAllText(path));
            if (cells is not null)
            {
                return new Dictionary<string, TileCell>(cells, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new Dictionary<string, TileCell>(StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(string path, IReadOnlyDictionary<string, TileCell> cells)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(cells));
        File.Move(tempPath, path, overwrite: true);
    }
}
