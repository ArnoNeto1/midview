using Midview.Protocol;
using Xunit;

public class SessionIdentityTests
{
    [Fact]
    public void LoadOrCreate_NoExistingFile_CreatesAndPersistsCertificate()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.pfx");
        try
        {
            var certificate = SessionIdentity.LoadOrCreate(tempPath);

            Assert.True(File.Exists(tempPath));
            Assert.NotEmpty(certificate.Thumbprint);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void LoadOrCreate_ExistingFile_ReturnsSameCertificateOnSecondCall()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.pfx");
        try
        {
            var first = SessionIdentity.LoadOrCreate(tempPath);
            var second = SessionIdentity.LoadOrCreate(tempPath);

            Assert.Equal(first.Thumbprint, second.Thumbprint);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void LoadOrCreate_ExistingFile_LoadsThePrivateKey()
    {
        // Session serves TLS with this certificate, which needs the private
        // key -- not just the public certificate -- after a reload.
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.pfx");
        try
        {
            SessionIdentity.LoadOrCreate(tempPath);
            var reloaded = SessionIdentity.LoadOrCreate(tempPath);

            Assert.True(reloaded.HasPrivateKey);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
