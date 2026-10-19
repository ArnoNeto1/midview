using System.IO;
using System.Text.Json;

namespace Midview.Master;

// How the teacher last left the classroom grid, so it opens the same way
// next time: the size of its tiles (TileSize) and whether it shows only the
// machines that are online (GridFilter).
public sealed record GridView(int TileWidth, bool OnlineOnly);

public static class GridViewFile
{
    public static string DefaultPath => Path.Combine(MasterData.Folder, "grid-view.json");

    // Missing or unreadable is the default: nothing in it is worth keeping
    // aside, unlike the classroom list. A file from before the filter has
    // only the size.
    public static GridView Load(string path)
    {
        try
        {
            var view = JsonSerializer.Deserialize<GridView>(File.ReadAllText(path));
            if (view is not null)
            {
                return view with { TileWidth = view.TileWidth > 0 ? TileSize.Snap(view.TileWidth) : TileSize.Default };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new GridView(TileSize.Default, OnlineOnly: false);
    }

    public static void Save(string path, GridView view)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(view));
        File.Move(tempPath, path, overwrite: true);
    }
}
