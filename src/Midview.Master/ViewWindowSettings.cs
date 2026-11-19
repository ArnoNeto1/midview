using System.IO;
using System.Text.Json;

namespace Midview.Master;

// How the student's screen window opens, for every student: PinToolbar is
// "Fixar a barra da tela do aluno" (Máquinas da sala > Avançado), the
// toolbar always shown above the picture instead of sliding away over it
// (ViewToolbarAutoHide).
public sealed record ViewWindowSettings(bool PinToolbar = false);

public static class ViewWindowSettingsFile
{
    public static string DefaultPath => Path.Combine(MasterData.Folder, "tela-do-aluno.json");

    // Missing or unreadable is the default: the toolbar slides away.
    public static ViewWindowSettings Load(string? path = null)
    {
        try
        {
            return JsonSerializer.Deserialize<ViewWindowSettings>(File.ReadAllText(path ?? DefaultPath)) ?? new ViewWindowSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ViewWindowSettings();
        }
    }

    public static void Save(ViewWindowSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings));
        File.Move(tempPath, path, overwrite: true);
    }
}
