using Midview.Protocol;
using Xunit;

public class SessionIdentityTests
{
    [Fact]
    public void EnsureExists_CalledTwice_KeepsTheSameThumbprint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-session-{Guid.NewGuid()}.pfx");
        try
        {
            var first = SessionIdentity.EnsureExists(path);
            var second = SessionIdentity.EnsureExists(path);

            Assert.Equal(first, second);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_AfterEnsureExists_HasThePrivateKeyAndTheSameThumbprint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-session-{Guid.NewGuid()}.pfx");
        try
        {
            var thumbprint = SessionIdentity.EnsureExists(path);

            using var certificate = SessionIdentity.Load(path);

            Assert.True(certificate.HasPrivateKey);
            Assert.Equal(thumbprint, certificate.Thumbprint);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
