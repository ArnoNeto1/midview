using System.Text.Json;

namespace Midview.Agent;

public sealed class SiteBlockState
{
    public bool InternetBlocked { get; set; }
    public HashSet<string> BlockedDomains { get; set; } = new();

    public static SiteBlockState Load(string statePath)
    {
        if (!File.Exists(statePath))
        {
            return new SiteBlockState();
        }

        var json = File.ReadAllText(statePath);
        return JsonSerializer.Deserialize<SiteBlockState>(json) ?? new SiteBlockState();
    }

    public void Save(string statePath)
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(statePath, JsonSerializer.Serialize(this));
    }
}
