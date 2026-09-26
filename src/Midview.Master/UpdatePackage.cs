using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Midview.Protocol;

namespace Midview.Master;

// The student program to push over the network (NetworkUpdate): the
// package's programa\aluno, exactly the files its midview-build.json lists
// plus the manifest itself, zipped into a temporary file that goes away
// with this object.
public sealed class UpdatePackage : IDisposable
{
    private UpdatePackage(string zipPath, BuildManifest manifest, byte[] fingerprint, byte[] zipSha256, long uncompressedSize)
    {
        ZipPath = zipPath;
        Manifest = manifest;
        Fingerprint = fingerprint;
        ZipSha256 = zipSha256;
        UncompressedSize = uncompressedSize;
    }

    public string ZipPath { get; }
    public BuildManifest Manifest { get; }
    public byte[] Fingerprint { get; }
    public byte[] ZipSha256 { get; }
    public long UncompressedSize { get; }
    public long ZipSize => new FileInfo(ZipPath).Length;

    public static string StudentProgramFolder(string packageDirectory) =>
        Path.Combine(packageDirectory, "programa", "aluno");

    // Just what identifies it, without zipping: for showing and comparing.
    // InvalidDataException, with the teacher's wording, for a package that
    // can't be pushed.
    public static (BuildManifest Manifest, byte[] Fingerprint) Identify(string packageDirectory)
    {
        var folder = StudentProgramFolder(packageDirectory);
        BuildManifest manifest;
        try
        {
            manifest = BuildManifest.Load(folder);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException(
                "Este pacote é antigo demais para atualizar pela rede (não tem midview-build.json): gere de novo com o Gerar-Pacote atual.", ex);
        }

        if (manifest.UpdateProtocol < NetworkUpdate.ProtocolVersion)
        {
            throw new InvalidDataException("Este pacote é antigo demais para atualizar pela rede: gere de novo com o Gerar-Pacote atual.");
        }
        var missing = NetworkUpdate.RequiredFiles
            .FirstOrDefault(required => !manifest.Files.Contains(required, StringComparer.OrdinalIgnoreCase));
        if (missing is not null)
        {
            throw new InvalidDataException($"O pacote não tem {missing}.");
        }

        try
        {
            return (manifest, PackageFingerprint.Compute(folder));
        }
        catch (FileNotFoundException ex)
        {
            throw new InvalidDataException($"O pacote está incompleto: {ex.Message}.", ex);
        }
    }

    public static UpdatePackage Create(string packageDirectory, string tempDirectory)
    {
        var (manifest, fingerprint) = Identify(packageDirectory);
        var folder = StudentProgramFolder(packageDirectory);
        Directory.CreateDirectory(tempDirectory);
        var zipPath = Path.Combine(tempDirectory, $"midview-update-{Guid.NewGuid():N}.zip");
        long uncompressed = 0;
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var relativePath in manifest.Files.Append(NetworkUpdate.ManifestFileName))
                {
                    var source = Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    uncompressed += new FileInfo(source).Length;
                    zip.CreateEntryFromFile(source, relativePath, CompressionLevel.Optimal);
                }
            }

            byte[] sha256;
            using (var stream = File.OpenRead(zipPath))
            {
                sha256 = SHA256.HashData(stream);
            }
            return new UpdatePackage(zipPath, manifest, fingerprint, sha256, uncompressed);
        }
        catch
        {
            File.Delete(zipPath);
            throw;
        }
    }

    public UpdateOffer Offer(byte[] attemptId) => new(attemptId, Fingerprint, ZipSize, ZipSha256, UncompressedSize);

    public void Dispose()
    {
        try
        {
            File.Delete(ZipPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp file; Windows cleans those up eventually.
        }
    }
}
