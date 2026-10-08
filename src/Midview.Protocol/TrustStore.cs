using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Midview.Protocol;

// The thumbprints a connection's other end must have (pinned, never a
// certificate chain). Either a fixed list, or trusted-masters.json itself
// (FromFile): read again whenever the file changes, so a teacher the Agent
// pairs with is trusted at once -- by the Agent, and by the Session in its
// own process -- without a restart.
public sealed class TrustStore
{
    private readonly object _gate = new();
    private readonly string? _path;
    private HashSet<string> _trustedThumbprints;
    private (DateTime WriteTimeUtc, long Length)? _loadedVersion;

    public TrustStore(IEnumerable<string> trustedThumbprints)
    {
        _trustedThumbprints = Normalize(trustedThumbprints);
    }

    private TrustStore(string path)
    {
        _path = path;
        _trustedThumbprints = new HashSet<string>();
    }

    public static TrustStore FromFile(string path) => new(path);

    // How many thumbprints are trusted right now (none: nobody paired yet).
    public int Count
    {
        get
        {
            lock (_gate)
            {
                RefreshIfChanged();
                return _trustedThumbprints.Count;
            }
        }
    }

    public bool IsTrusted(X509Certificate certificate)
    {
        using var certificate2 = new X509Certificate2(certificate);
        return IsTrusted(certificate2.Thumbprint);
    }

    public bool IsTrusted(string thumbprint)
    {
        lock (_gate)
        {
            RefreshIfChanged();
            return _trustedThumbprints.Contains(thumbprint.Trim().ToUpperInvariant());
        }
    }

    // The file's write time and size say whether it changed, so a check
    // costs one look at the file, not a read of it. Two writes of the same
    // size within one tick of the file clock (0.5 to about 16 ms, depending
    // on the PC) look like no change; nothing writes it that way, since the
    // Agent only adds teachers, which always makes it longer.
    private void RefreshIfChanged()
    {
        if (_path is null)
        {
            return;
        }

        var file = new FileInfo(_path);
        (DateTime, long)? version = file.Exists ? (file.LastWriteTimeUtc, file.Length) : null;
        if (Nullable.Equals(version, _loadedVersion))
        {
            return;
        }

        try
        {
            _trustedThumbprints = version is null ? new HashSet<string>() : Normalize(TrustedMastersFile.Load(_path));
            _loadedVersion = version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Being written or locked right now: keep trusting whom we
            // trusted, and read it again next time.
        }
    }

    private static HashSet<string> Normalize(IEnumerable<string?> thumbprints) =>
        thumbprints
            .Where(thumbprint => !string.IsNullOrWhiteSpace(thumbprint))
            .Select(thumbprint => thumbprint!.Trim().ToUpperInvariant())
            .ToHashSet();
}
