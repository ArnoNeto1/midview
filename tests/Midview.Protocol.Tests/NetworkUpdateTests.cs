using System.Text;
using Midview.Protocol;
using Xunit;

public class NetworkUpdateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"midview-build-{Guid.NewGuid():N}");

    public NetworkUpdateTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Offer_RoundTrips()
    {
        var offer = new UpdateOffer(Bytes(16, 1), Bytes(32, 2), 71_000_000, Bytes(32, 3), 163_000_000);

        Assert.True(NetworkUpdate.TryDecodeOffer(NetworkUpdate.EncodeOffer(offer), out var decoded));
        Assert.Equal(offer.AttemptId, decoded.AttemptId);
        Assert.Equal(offer.Fingerprint, decoded.Fingerprint);
        Assert.Equal(offer.ZipSize, decoded.ZipSize);
        Assert.Equal(offer.ZipSha256, decoded.ZipSha256);
        Assert.Equal(offer.UncompressedSize, decoded.UncompressedSize);
    }

    [Fact]
    public void Offer_WrongLengthOrEmptySizes_AreRefused()
    {
        Assert.False(NetworkUpdate.TryDecodeOffer(new byte[NetworkUpdate.OfferLength - 1], out _));
        Assert.False(NetworkUpdate.TryDecodeOffer(new byte[NetworkUpdate.OfferLength], out _)); // sizes of 0
    }

    [Theory]
    [InlineData("Midview.Agent.exe", "Midview.Agent.exe")]
    [InlineData(@"runtimes\win-x64\native\x.dll", "runtimes/win-x64/native/x.dll")]
    [InlineData("pt-BR/Midview.resources.dll", "pt-BR/Midview.resources.dll")]
    public void ValidRelativePaths_AreNormalized(string path, string expected)
    {
        Assert.True(NetworkUpdate.IsValidRelativePath(path, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Midview.Agent.exe")]
    [InlineData(@"\Midview.Agent.exe")]
    [InlineData(@"C:\Windows\x.exe")]
    [InlineData("../x.exe")]
    [InlineData("a/../../x.exe")]
    [InlineData("a/./x.exe")]
    [InlineData("a//x.exe")]
    [InlineData("x.exe:stream")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("x.exe.")]
    [InlineData(" x.exe")]
    [InlineData("a|b.dll")]
    public void UnsafeRelativePaths_AreRefused(string path) =>
        Assert.False(NetworkUpdate.IsValidRelativePath(path, out _));

    [Fact]
    public void TooDeepAPath_IsRefused() =>
        Assert.False(NetworkUpdate.IsValidRelativePath(string.Join('/', Enumerable.Repeat("a", NetworkUpdate.MaxPathDepth + 1)), out _));

    [Fact]
    public void MasterDeadline_CoversAFullApplyAndRollback() =>
        Assert.True(NetworkUpdate.MasterDeadline >
            NetworkUpdate.VerifyAllowance + NetworkUpdate.ConfirmationWindow + 2 * (NetworkUpdate.ServiceStopTimeout + NetworkUpdate.ServiceStartTimeout));

    [Fact]
    public void StatusReport_RoundTrips()
    {
        var report = new UpdateStatusReport("AB12", "2026-09-26 19:40 a1b2c3d", true,
            new UpdateAttemptReport("00FF", UpdateStates.Committed, "atualizada", new DateTimeOffset(2026, 9, 26, 20, 0, 0, TimeSpan.Zero)));

        Assert.True(UpdateStatusReport.TryDecode(report.Encode(), out var decoded));
        Assert.Equal(report, decoded);
    }

    [Fact]
    public void StatusReport_Garbage_IsRefused() =>
        Assert.False(UpdateStatusReport.TryDecode(Encoding.UTF8.GetBytes("{ não"), out _));

    [Fact]
    public void FinalStates_AreTheLastThree()
    {
        Assert.True(UpdateStates.IsFinal(UpdateStates.Committed));
        Assert.True(UpdateStates.IsFinal(UpdateStates.RolledBack));
        Assert.True(UpdateStates.IsFinal(UpdateStates.Failed));
        Assert.False(UpdateStates.IsFinal(UpdateStates.Provisional));
        Assert.False(UpdateStates.IsFinal(null));
    }

    [Fact]
    public void Manifest_ParsesAndNormalizesItsFiles()
    {
        var manifest = BuildManifest.Parse(Manifest("Midview.Agent.exe", @"runtimes\x.dll"));

        Assert.Equal("2026-09-26 19:40 a1b2c3d", manifest.Build);
        Assert.Equal(1, manifest.UpdateProtocol);
        Assert.Equal(new[] { "Midview.Agent.exe", "runtimes/x.dll" }, manifest.Files);
    }

    [Theory]
    [InlineData("{ não é json")]
    [InlineData("""{ "build": "", "updateProtocol": 1, "files": ["a.dll"] }""")]
    [InlineData("""{ "build": "b", "updateProtocol": 1, "files": [] }""")]
    [InlineData("""{ "build": "b", "updateProtocol": 1, "files": ["../a.dll"] }""")]
    [InlineData("""{ "build": "b", "updateProtocol": 1, "files": ["a.dll", "A.DLL"] }""")]
    [InlineData("""{ "build": "b", "updateProtocol": 1, "files": ["midview-build.json"] }""")]
    public void Manifest_BadOnes_AreRefused(string json) =>
        Assert.Throws<InvalidDataException>(() => BuildManifest.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Fingerprint_IsTheSameForTheSameBuild_AndIgnoresUnlistedFiles()
    {
        WriteBuild(("Midview.Agent.exe", "agent"), ("runtimes/x.dll", "native"));
        var first = PackageFingerprint.Compute(_folder);

        File.WriteAllText(Path.Combine(_folder, "session-perf.log"), "a log that appears later");
        File.SetLastWriteTimeUtc(Path.Combine(_folder, "Midview.Agent.exe"), DateTime.UtcNow.AddDays(-3));

        Assert.Equal(first, PackageFingerprint.Compute(_folder));
    }

    [Fact]
    public void Fingerprint_ChangesWithAFilesContent()
    {
        WriteBuild(("Midview.Agent.exe", "agent"));
        var first = PackageFingerprint.Compute(_folder);

        File.WriteAllText(Path.Combine(_folder, "Midview.Agent.exe"), "agenT");

        Assert.NotEqual(first, PackageFingerprint.Compute(_folder));
    }

    [Fact]
    public void Fingerprint_ChangesWithAFilesName()
    {
        var a = PackageFingerprint.Combine(new[] { new PackageFingerprint.FileEntry("a.dll", 1, Bytes(32, 9)) });
        var b = PackageFingerprint.Combine(new[] { new PackageFingerprint.FileEntry("b.dll", 1, Bytes(32, 9)) });

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Fingerprint_IgnoresOrderSeparatorsAndCase()
    {
        var one = PackageFingerprint.Combine(new[]
        {
            new PackageFingerprint.FileEntry("runtimes/X.dll", 1, Bytes(32, 1)),
            new PackageFingerprint.FileEntry("a.exe", 2, Bytes(32, 2)),
        });
        var other = PackageFingerprint.Combine(new[]
        {
            new PackageFingerprint.FileEntry("A.EXE", 2, Bytes(32, 2)),
            new PackageFingerprint.FileEntry(@"runtimes\x.dll", 1, Bytes(32, 1)),
        });

        Assert.Equal(one, other);
    }

    [Fact]
    public void Fingerprint_AListedFileMissing_Fails()
    {
        WriteBuild(("Midview.Agent.exe", "agent"));
        File.Delete(Path.Combine(_folder, "Midview.Agent.exe"));

        Assert.Throws<FileNotFoundException>(() => PackageFingerprint.Compute(_folder));
    }

    private void WriteBuild(params (string Path, string Content)[] files)
    {
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(_folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        File.WriteAllBytes(Path.Combine(_folder, NetworkUpdate.ManifestFileName), Manifest(files.Select(file => file.Path).ToArray()));
    }

    private static byte[] Manifest(params string[] files) => Encoding.UTF8.GetBytes(
        $$"""{ "build": "2026-09-26 19:40 a1b2c3d", "updateProtocol": 1, "files": [{{string.Join(", ", files.Select(file => System.Text.Json.JsonSerializer.Serialize(file)))}}] }""");

    private static byte[] Bytes(int length, byte value) => Enumerable.Repeat(value, length).ToArray();
}
