using System.IO;
using System.Text.Json;

namespace Midview.Master;

// How the teacher last left the classroom grid -- the size of its tiles
// (TileSize) -- so it opens the same way next time.
public static class GridViewFile
{
    public static string DefaultPath => Path.Combine(MasterData.Folder, "grid-view.json");

    // Missing or unreadable is the default size: nothing in it is worth
    // keeping aside, unlike the classroom list.
    public static int LoadTileWidth(string path)
    {
        try
        {
            var view = JsonSerializer.Deserialize<GridView>(File.ReadAllText(path));
            return view is { TileWidth: > 0 } ? TileSize.Snap(view.TileWidth) : TileSize.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return TileSize.Default;
        }
    }

    public static void SaveTileWidth(string path, int tileWidth)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(new GridView(tileWidth)));
        File.Move(tempPath, path, overwrite: true);
    }

    private sealed record GridView(int TileWidth);
}
