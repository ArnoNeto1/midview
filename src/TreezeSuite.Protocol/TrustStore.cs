using System.Security.Cryptography.X509Certificates;

namespace TreezeSuite.Protocol;

public sealed class TrustStore
{
    private readonly HashSet<string> _trustedThumbprints;

    public TrustStore(IEnumerable<string> trustedThumbprints)
    {
        _trustedThumbprints = trustedThumbprints
            .Select(t => t.ToUpperInvariant())
            .ToHashSet();
    }

    public bool IsTrusted(X509Certificate certificate)
    {
        using var certificate2 = new X509Certificate2(certificate);
        return _trustedThumbprints.Contains(certificate2.Thumbprint.ToUpperInvariant());
    }
}
