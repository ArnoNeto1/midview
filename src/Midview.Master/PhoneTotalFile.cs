using System.IO;

namespace Midview.Master;

// How many phones the school has, for "Celular 07 pronto ✓ (3 de 11)": asked
// once by the assistant, optional. The file existing means it was asked; an
// empty one means the teacher left it blank (and is not asked again).
public static class PhoneTotalFile
{
    private static string PathIn(string? folder) => Path.Combine(folder ?? MasterData.Folder, "phone-total.txt");

    public static bool Asked(string? folder = null) => File.Exists(PathIn(folder));

    // Null when not asked, left blank, or unreadable; never throws for those.
    public static int? Load(string? folder = null)
    {
        try
        {
            return Parse(File.ReadAllText(PathIn(folder)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(int? total, string? folder = null)
    {
        var path = PathIn(folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, total?.ToString() ?? "");
    }

    // 1 to 999 phones; anything else is no total.
    public static int? Parse(string typed) =>
        int.TryParse((typed ?? "").Trim(), System.Globalization.NumberStyles.None, null, out var total) && total is >= 1 and <= 999
            ? total
            : null;
}
