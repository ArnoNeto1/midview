using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class UpdatePackageTests : IDisposable
{
    private readonly string _package = Path.Combine(Path.GetTempPath(), $"midview-update-package-{Guid.NewGuid():N}");
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"midview-update-temp-{Guid.NewGuid():N}");

    public UpdatePackageTests()
    {
        Directory.CreateDirectory(Folder);
    }

    public void Dispose()
    {
        Directory.Delete(_package, recursive: true);
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private string Folder => UpdatePackage.StudentProgramFolder(_package);

    private void WriteProgram(params string[] listed) => WriteProgram(listed, NetworkUpdate.ProtocolVersion);

    private void WriteProgram(string[] listed, int protocol)
    {
        foreach (var relativePath in listed)
        {
            var path = Path.Combine(Folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"conteúdo de {relativePath}");
        }
        File.WriteAllText(Path.Combine(Folder, NetworkUpdate.ManifestFileName), JsonSerializer.Serialize(new
        {
            build = "2026-09-26 19:40 a1b2c3d",
            updateProtocol = protocol,
            files = listed,
        }));
    }

    private static readonly string[] Program = { "Midview.Agent.exe", "Midview.Session.exe", "runtimes/win-x64/native.dll" };

    [Fact]
    public void Identify_ReadsTheManifest_AndFingerprintsTheFolder()
    {
        WriteProgram(Program);

        var (manifest, fingerprint) = UpdatePackage.Identify(_package);

        Assert.Equal("2026-09-26 19:40 a1b2c3d", manifest.Build);
        Assert.Equal(Program, manifest.Files);
        Assert.Equal(PackageFingerprint.Compute(Folder), fingerprint);
    }

    [Fact]
    public void Identify_APackageWithoutManifest_IsTooOld()
    {
        File.WriteAllText(Path.Combine(Folder, "Midview.Agent.exe"), "x");

        var error = Assert.Throws<InvalidDataException>(() => UpdatePackage.Identify(_package));

        Assert.Contains("antigo demais", error.Message);
    }

    [Fact]
    public void Identify_APackageWithoutTheStudentProgram_IsTooOld()
    {
        Directory.Delete(Folder);

        var error = Assert.Throws<InvalidDataException>(() => UpdatePackage.Identify(_package));

        Assert.Contains("antigo demais", error.Message);
    }

    [Fact]
    public void Identify_AnOlderProtocol_IsTooOld()
    {
        WriteProgram(Program, NetworkUpdate.ProtocolVersion - 1);

        var error = Assert.Throws<InvalidDataException>(() => UpdatePackage.Identify(_package));

        Assert.Contains("antigo demais", error.Message);
    }

    [Fact]
    public void Identify_WithoutTheSession_IsRefused()
    {
        WriteProgram("Midview.Agent.exe", "Midview.Protocol.dll");

        var error = Assert.Throws<InvalidDataException>(() => UpdatePackage.Identify(_package));

        Assert.Equal("O pacote não tem Midview.Session.exe.", error.Message);
    }

    [Fact]
    public void Identify_AListedFileThatIsMissing_IsIncomplete()
    {
        WriteProgram(Program);
        File.Delete(Path.Combine(Folder, "Midview.Session.exe"));

        var error = Assert.Throws<InvalidDataException>(() => UpdatePackage.Identify(_package));

        Assert.StartsWith("O pacote está incompleto", error.Message);
    }

    [Fact]
    public void Create_ZipsExactlyTheListedFilesAndTheManifest()
    {
        WriteProgram(Program);
        File.WriteAllText(Path.Combine(Folder, "log-que-sobrou.txt"), "não vai");

        using var package = UpdatePackage.Create(_package, _temp);

        using var zip = ZipFile.OpenRead(package.ZipPath);
        Assert.Equal(
            Program.Append(NetworkUpdate.ManifestFileName).Order(StringComparer.Ordinal),
            zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            var expected = File.ReadAllText(Path.Combine(Folder, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(expected, reader.ReadToEnd());
        }
    }

    [Fact]
    public void Create_DescribesTheZipInTheOffer()
    {
        WriteProgram(Program);
        var listed = Program.Append(NetworkUpdate.ManifestFileName)
            .Sum(relativePath => new FileInfo(Path.Combine(Folder, relativePath.Replace('/', Path.DirectorySeparatorChar))).Length);

        using var package = UpdatePackage.Create(_package, _temp);
        var attemptId = RandomNumberGenerator.GetBytes(NetworkUpdate.AttemptIdLength);
        var offer = package.Offer(attemptId);

        Assert.Equal(attemptId, offer.AttemptId);
        Assert.Equal(PackageFingerprint.Compute(Folder), offer.Fingerprint);
        Assert.Equal(new FileInfo(package.ZipPath).Length, offer.ZipSize);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(package.ZipPath)), offer.ZipSha256);
        Assert.Equal(listed, offer.UncompressedSize);
    }

    [Fact]
    public void Dispose_DeletesTheZip()
    {
        WriteProgram(Program);
        var package = UpdatePackage.Create(_package, _temp);
        Assert.True(File.Exists(package.ZipPath));

        package.Dispose();

        Assert.False(File.Exists(package.ZipPath));
    }

    [Fact]
    public void Create_ForABadPackage_LeavesNoZipBehind()
    {
        WriteProgram("Midview.Agent.exe");

        Assert.Throws<InvalidDataException>(() => UpdatePackage.Create(_package, _temp));

        Assert.True(!Directory.Exists(_temp) || Directory.GetFiles(_temp).Length == 0);
    }
}
