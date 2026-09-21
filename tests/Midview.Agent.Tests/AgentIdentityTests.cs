using Midview.Agent;
using Xunit;

public class AgentIdentityTests
{
    [Fact]
    public void LoadOrCreate_NoExistingFile_CreatesAndPersistsCertificate()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.pfx");
        try
        {
            var certificate = AgentIdentity.LoadOrCreate(tempPath);

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
            var first = AgentIdentity.LoadOrCreate(tempPath);
            var second = AgentIdentity.LoadOrCreate(tempPath);

            Assert.Equal(first.Thumbprint, second.Thumbprint);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
