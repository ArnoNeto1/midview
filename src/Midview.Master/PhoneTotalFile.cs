using System.IO;

namespace Midview.Master;

// How many phones the school has, for "Celular 07 pronto ✓ (3 de 11)": asked
// once by the assistant, optional. The file existing means it was asked; an
// empty one means the teacher left it blank (and is not asked again).
// Tablets have a total of their own (tablet-total.txt), asked the first time
// the assistant prepares a tablet: a school's tablets are not its phones.
public static class PhoneTotalFile
{
    private static string PathIn(string? folder, AndroidDeviceKind kind) =>
        Path.Combine(folder ?? MasterData.Folder, kind == AndroidDeviceKind.Tablet ? "tablet-total.txt" : "phone-total.txt");

    public static bool Asked(string? folder = null, AndroidDeviceKind kind = AndroidDeviceKind.Phone) =>
        File.Exists(PathIn(folder, kind));

    // Null when not asked, left blank, or unreadable; never throws for those.
    public static int? Load(string? folder = null, AndroidDeviceKind kind = AndroidDeviceKind.Phone)
    {
        try
        {
            return Parse(File.ReadAllText(PathIn(folder, kind)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(int? total, string? folder = null, AndroidDeviceKind kind = AndroidDeviceKind.Phone)
    {
        var path = PathIn(folder, kind);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, total?.ToString() ?? "");
    }

    // 1 to 999 devices; anything else is no total.
    public static int? Parse(string typed) =>
        int.TryParse((typed ?? "").Trim(), System.Globalization.NumberStyles.None, null, out var total) && total is >= 1 and <= 999
            ? total
            : null;
}
