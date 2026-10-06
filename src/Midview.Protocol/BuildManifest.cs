using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midview.Protocol;

// midview-build.json, written by installer/Gerar-Instalador.ps1 next to the
// student program: a readable label for the build (date and git commit),
// the update protocol it speaks, and the list of its files. What identifies
// a build (PackageFingerprint) is exactly that list, so files that appear
// next to a running build later (a log, say) never change it.
public sealed record BuildManifest(
    [property: JsonPropertyName("build")] string Build,
    [property: JsonPropertyName("updateProtocol")] int UpdateProtocol,
    [property: JsonPropertyName("files")] IReadOnlyList<string> Files)
{
    // The manifest's files, each normalized ('/'-separated), as validated
    // by Parse.
    public static BuildManifest Parse(byte[] json)
    {
        BuildManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BuildManifest>(new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(json));
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or NotSupportedException)
        {
            throw new InvalidDataException($"{NetworkUpdate.ManifestFileName} não pôde ser lido: {ErrorText.Describe(ex)}", ex);
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Build) || manifest.Files is null || manifest.Files.Count == 0)
        {
            throw new InvalidDataException($"{NetworkUpdate.ManifestFileName} está incompleto");
        }
        if (manifest.Files.Count > NetworkUpdate.MaxEntries)
        {
            throw new InvalidDataException($"{NetworkUpdate.ManifestFileName} lista arquivos demais");
        }

        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NetworkUpdate.ManifestFileName };
        foreach (var file in manifest.Files)
        {
            if (file is null || !NetworkUpdate.IsValidRelativePath(file, out var normalized) || !seen.Add(normalized))
            {
                throw new InvalidDataException($"{NetworkUpdate.ManifestFileName} tem um caminho inválido ou repetido: \"{file}\"");
            }
            files.Add(normalized);
        }

        return manifest with { Files = files };
    }

    public static BuildManifest Load(string folder) =>
        Parse(File.ReadAllBytes(Path.Combine(folder, NetworkUpdate.ManifestFileName)));
}

// What identifies a build of the student program: SHA-256 over its manifest
// and exactly the files the manifest lists, in a fixed order (relative paths
// lower-cased, sorted), each contributing its path, its length and the
// SHA-256 of its content. The same build gives the same fingerprint in the
// package folder, in a zip's extracted copy and in an installed folder,
// whatever the files' timestamps.
public static class PackageFingerprint
{
    public sealed record FileEntry(string RelativePath, long Length, byte[] Sha256);

    // Of the build in folder; FileNotFoundException for a listed file that
    // isn't there, IOException for one that's a link rather than a file.
    public static byte[] Compute(string folder)
    {
        var manifest = BuildManifest.Load(folder);
        var entries = new List<FileEntry>();
        foreach (var relativePath in manifest.Files.Append(NetworkUpdate.ManifestFileName))
        {
            entries.Add(HashFile(Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar)), relativePath));
        }
        return Combine(entries);
    }

    public static FileEntry HashFile(string path, string relativePath)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"falta {relativePath}", path);
        }
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException($"{relativePath} é um atalho, não um arquivo");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return new FileEntry(relativePath, stream.Length, SHA256.HashData(stream));
    }

    public static byte[] Combine(IEnumerable<FileEntry> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (var file in files
                     .Select(file => file with { RelativePath = file.RelativePath.Replace('\\', '/').ToLowerInvariant() })
                     .OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.RelativePath));
            hash.AppendData(new byte[] { 0 });
            BinaryPrimitives.WriteInt64BigEndian(length, file.Length);
            hash.AppendData(length);
            hash.AppendData(file.Sha256);
        }
        return hash.GetHashAndReset();
    }

    public static string ToHex(byte[] fingerprint) => Convert.ToHexString(fingerprint);
}
