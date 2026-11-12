using Midview.Master;
using Xunit;

public class ApkSignatureTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-apksig-" + Guid.NewGuid().ToString("N"));

    public ApkSignatureTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(byte[] bytes)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".apk");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void V2_block_gives_the_certificate_checksum()
    {
        var cert = ApkTestBuilder.NewCertificate("v2");
        var path = Write(ApkTestBuilder.Apk((ApkTestBuilder.V2Id, ApkTestBuilder.SignerBlock(cert))));

        Assert.Equal(ApkTestBuilder.Checksum(cert), ApkSignature.SignerChecksum(path));
    }

    [Fact]
    public void V3_wins_over_v2()
    {
        var v2 = ApkTestBuilder.NewCertificate("v2");
        var v3 = ApkTestBuilder.NewCertificate("v3");
        var path = Write(ApkTestBuilder.Apk(
            (ApkTestBuilder.V2Id, ApkTestBuilder.SignerBlock(v2)),
            (ApkTestBuilder.V3Id, ApkTestBuilder.SignerBlock(v3))));

        Assert.Equal(ApkTestBuilder.Checksum(v3), ApkSignature.SignerChecksum(path));
    }

    [Fact]
    public void Zip_without_block_gives_null()
    {
        Assert.Null(ApkSignature.SignerChecksum(Write(ApkTestBuilder.ZipWithoutBlock())));
    }

    [Fact]
    public void Truncated_and_garbage_bytes_give_null()
    {
        var cert = ApkTestBuilder.NewCertificate("t");
        var apk = ApkTestBuilder.Apk((ApkTestBuilder.V2Id, ApkTestBuilder.SignerBlock(cert)));

        Assert.Null(ApkSignature.SignerChecksum(Write(apk[..(apk.Length - 30)])));
        Assert.Null(ApkSignature.SignerChecksum(Write(new byte[] { 1, 2, 3 })));
        Assert.Null(ApkSignature.SignerChecksum(Write(Array.Empty<byte>())));
        Assert.Null(ApkSignature.SignerChecksum(Path.Combine(_folder, "missing.apk")));
    }

    [Fact]
    public void Corrupt_lengths_inside_the_block_give_null()
    {
        var bad = new byte[] { 0xff, 0xff, 0xff, 0x7f, 1, 2, 3 };

        Assert.Null(ApkSignature.SignerChecksum(Write(ApkTestBuilder.Apk((ApkTestBuilder.V2Id, bad)))));
    }
}
