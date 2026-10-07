using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Midview.Protocol;

namespace Midview.Master;

// The student program to push over the network (NetworkUpdate): a folder
// with it (the one the setup installs next to the Master, see
// BundledStudentFolder), exactly the files its midview-build.json lists
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

    // The student program the setup installs next to the Master
    // ("C:\Program Files\Midview Professor\aluno"): what "Atualizar as
    // máquinas" sends. Missing in a development build.
    public static string BundledStudentFolder => Path.Combine(AppContext.BaseDirectory, "aluno");

    // Just what identifies it, without zipping: for showing and comparing.
    // InvalidDataException, with the teacher's wording, for a program that
    // can't be pushed.
    public static (BuildManifest Manifest, byte[] Fingerprint) Identify(string studentFolder)
    {
        var folder = studentFolder;
        BuildManifest manifest;
        try
        {
            manifest = BuildManifest.Load(folder);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException(
                "Este programa do aluno é antigo demais para atualizar pela rede (não tem midview-build.json): instale de novo com o MidviewSetup atual.", ex);
        }

        if (manifest.UpdateProtocol < NetworkUpdate.ProtocolVersion)
        {
            throw new InvalidDataException("Este programa do aluno é antigo demais para atualizar pela rede: instale de novo com o MidviewSetup atual.");
        }
        var missing = NetworkUpdate.RequiredFiles
            .FirstOrDefault(required => !manifest.Files.Contains(required, StringComparer.OrdinalIgnoreCase));
        if (missing is not null)
        {
            throw new InvalidDataException($"O programa do aluno não tem {missing}.");
        }

        try
        {
            return (manifest, PackageFingerprint.Compute(folder));
        }
        catch (FileNotFoundException ex)
        {
            throw new InvalidDataException($"O programa do aluno está incompleto: {ErrorText.Describe(ex)}.", ex);
        }
    }

    public static UpdatePackage Create(string studentFolder, string tempDirectory)
    {
        var (manifest, fingerprint) = Identify(studentFolder);
        var folder = studentFolder;
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
