using System.Text.Json;

namespace Midview.Protocol;

// %ProgramData%\Midview\trusted-masters.json: the thumbprints of the
// teachers this machine trusts, as a JSON list of strings. The Agent adds
// to it when a teacher pairs, and cuts it to one teacher at "Esquecer os
// outros professores"; the Agent and the Session read it
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
        var updated = LoadForChange(path)
            .Append(thumbprint)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        Write(path, updated);
        return updated;
    }

    // "Esquecer os outros professores": the list becomes this teacher alone.
    // Returns how many others were dropped; with none the file is left as it
    // is. Null, and the file left alone, when this teacher is no longer on
    // the list (another one made the machine forget it a moment ago): it
    // must not come back as the only one.
    public static int? KeepOnly(string path, string thumbprint)
    {
        var kept = thumbprint.Trim().ToUpperInvariant();
        var current = LoadForChange(path)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
        if (!current.Contains(kept))
        {
            return null;
        }
        var dropped = current.Count(item => item != kept);
        if (dropped > 0)
        {
            Write(path, new List<string> { kept });
        }
        return dropped;
    }

    // A list that can't be read is left alone (InvalidDataException).
    private static IReadOnlyList<string> LoadForChange(string path)
    {
        try
        {
            return Load(path);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"a lista de professores desta máquina não pôde ser lida ({ErrorText.Describe(ex)})", ex);
        }
    }

    private static void Write(string path, List<string> teachers)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(teachers));
        File.Move(tempPath, path, overwrite: true);
    }
}
