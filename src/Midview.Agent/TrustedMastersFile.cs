using System.Text.Json;

namespace Midview.Agent;

public static class TrustedMastersFile
{
    public static IReadOnlyList<string> Load(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }
}
