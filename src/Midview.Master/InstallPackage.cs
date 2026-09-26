using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Midview.Protocol;

namespace Midview.Master;

// The installation package the scripts in installer/ build and use, usually
// on a USB stick. Two files connect it to Master:
//   - trusted-masters.json: the teachers every machine installed from it
//     trusts (the installer copies it as is to the machine);
//   - maquinas\<computer>.json: one per installed machine, written by the
//     installer, for the teacher to import into the classroom list.
// The thumbprints travel on the stick, from the machine itself: nothing is
// trusted over the network.
public static class InstallPackage
{
    public const string TrustedMastersFileName = "trusted-masters.json";
    public const string MachinesDirectoryName = "maquinas";
    public const string StudentInstallerFileName = "Instalar-Aluno.cmd";

    private static readonly Regex ThumbprintPattern = new("^[0-9A-F]{40}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static bool IsPackageDirectory(string directory) =>
        File.Exists(Path.Combine(directory, StudentInstallerFileName));

    // Keeps the teachers already authorized in the package (another teacher's
    // PC, say) and returns the resulting list. An unreadable file is reported,
    // not replaced: it may hold another teacher's authorization.
    public static IReadOnlyList<string> AuthorizeMaster(string packageDirectory, string thumbprint)
    {
        var path = Path.Combine(packageDirectory, TrustedMastersFileName);
        var authorized = new List<string>();
        if (File.Exists(path))
        {
            try
            {
                authorized = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"{TrustedMastersFileName} no pacote não pôde ser lido ({ex.Message}). Apague o arquivo e autorize de novo.", ex);
            }
        }

        var normalized = NormalizeThumbprint(thumbprint)
            ?? throw new ArgumentException("thumbprint inválido", nameof(thumbprint));
        authorized = authorized
            .Select(NormalizeThumbprint)
            .OfType<string>()
            .Append(normalized)
            .Distinct()
            .ToList();

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(authorized, WriteOptions));
        File.Move(tempPath, path, overwrite: true);
        return authorized;
    }

    // One KnownMachine per valid file in maquinas\, in the classroom's order
    // (Aluno-2 before Aluno-10), plus one line per file that couldn't be used.
    public static (IReadOnlyList<KnownMachine> Machines, IReadOnlyList<string> Problems) LoadInstalledMachines(
        string packageDirectory, bool useComputerName)
    {
        var directory = Path.Combine(packageDirectory, MachinesDirectoryName);
        var machines = new Dictionary<string, KnownMachine>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        if (!Directory.Exists(directory))
        {
            return (Array.Empty<KnownMachine>(), problems);
        }

        foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(file);
            try
            {
                var record = JsonSerializer.Deserialize<InstalledMachineRecord>(File.ReadAllText(file));
                var (machine, problem) = ToKnownMachine(record, useComputerName);
                if (machine is null)
                {
                    problems.Add($"{fileName}: {problem}");
                }
                else
                {
                    machines[machine.Name] = machine;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{fileName}: não pôde ser lido ({ex.Message})");
            }
        }

        var ordered = machines.Values.OrderBy(machine => machine.Name, NaturalOrder.Instance).ToList();
        return (ordered, problems);
    }

    public static string? NormalizeThumbprint(string? thumbprint)
    {
        var normalized = thumbprint?.Trim().ToUpperInvariant();
        return normalized is not null && ThumbprintPattern.IsMatch(normalized) ? normalized : null;
    }

    private static (KnownMachine? Machine, string? Problem) ToKnownMachine(InstalledMachineRecord? record, bool useComputerName)
    {
        var name = record?.ComputerName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return (null, "sem o nome do computador");
        }

        var sessionThumbprint = NormalizeThumbprint(record!.SessionThumbprint);
        if (sessionThumbprint is null)
        {
            return (null, "thumbprint da Session inválido");
        }

        // Optional in the roster (site blocking just won't work without it),
        // but the installer always records it, so a bad one means a bad file.
        var agentThumbprint = NormalizeThumbprint(record.AgentThumbprint);
        if (record.AgentThumbprint is not null && agentThumbprint is null)
        {
            return (null, "thumbprint do Agent inválido");
        }

        var host = useComputerName
            ? name
            : record.Addresses?.Select(address => address?.Trim()).FirstOrDefault(address => !string.IsNullOrEmpty(address));
        if (host is null)
        {
            return (null, "sem endereço IP (marque \"usar o nome do computador\" para importar pelo nome)");
        }

        // Optional: machine files from before it was recorded don't have it,
        // and without it Master learns it from the Agent anyway.
        var macAddress = WakeOnLan.NormalizeMacAddress(record.MacAddress);

        return (new KnownMachine(name, host, sessionThumbprint, agentThumbprint, macAddress), null);
    }

    // What installer\scripts\Instalar-Aluno.ps1 writes.
    private sealed record InstalledMachineRecord(
        [property: JsonPropertyName("computerName")] string? ComputerName,
        [property: JsonPropertyName("addresses"), JsonConverter(typeof(StringOrListConverter))] List<string?>? Addresses,
        [property: JsonPropertyName("sessionThumbprint")] string? SessionThumbprint,
        [property: JsonPropertyName("agentThumbprint")] string? AgentThumbprint,
        [property: JsonPropertyName("macAddress")] string? MacAddress);

    // Windows PowerShell can write a one-item list as a plain string.
    private sealed class StringOrListConverter : JsonConverter<List<string?>>
    {
        public override List<string?>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String
                ? new List<string?> { reader.GetString() }
                : JsonSerializer.Deserialize<List<string?>>(ref reader, options);

        public override void Write(Utf8JsonWriter writer, List<string?> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    // "Aluno-2" before "Aluno-10": runs of digits compare as numbers.
    private sealed class NaturalOrder : IComparer<string>
    {
        public static readonly NaturalOrder Instance = new();

        private static readonly Regex Parts = new(@"\d+|\D+", RegexOptions.CultureInvariant);

        public int Compare(string? x, string? y)
        {
            var left = Parts.Matches(x ?? string.Empty);
            var right = Parts.Matches(y ?? string.Empty);
            for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
            {
                var a = left[i].Value;
                var b = right[i].Value;
                var result = char.IsDigit(a[0]) && char.IsDigit(b[0])
                    ? CompareNumbers(a, b)
                    : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                if (result != 0)
                {
                    return result;
                }
            }
            return left.Count.CompareTo(right.Count);
        }

        // By length first once leading zeros are gone, so any number of
        // digits works without overflowing.
        private static int CompareNumbers(string a, string b)
        {
            a = a.TrimStart('0');
            b = b.TrimStart('0');
            return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
        }
    }
}
