using System.Text.Json;

namespace Midview.Protocol;

// %ProgramData%\Midview\trusted-masters.json: the thumbprints of the
// teachers this machine trusts, as a JSON list of strings. The Agent adds
// to it when a teacher pairs; the Agent and the Session read it
// (TrustStore.FromFile).
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

    // Adds a teacher and returns the whole list: the others are kept, each
    // upper-cased and only once. Written to a temporary file and moved over
    // the old one, so the Session, reading while the Agent writes, never
    // sees half a list. A list that can't be read is left alone
    // (InvalidDataException): it holds the teachers this machine trusts.
    public static IReadOnlyList<string> Add(string path, string thumbprint)
    {
        IReadOnlyList<string> current;
        try
        {
            current = Load(path);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"a lista de professores desta máquina não pôde ser lida ({ErrorText.Describe(ex)})", ex);
        }

        var updated = current
            .Append(thumbprint)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(updated));
        File.Move(tempPath, path, overwrite: true);
        return updated;
    }
}
