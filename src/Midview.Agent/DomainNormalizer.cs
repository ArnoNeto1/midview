namespace Midview.Agent;

public static class DomainNormalizer
{
    // The Master sends whatever the teacher typed, unvalidated: normalizing
    // and rejecting anything that isn't a plain hostname here (not at the
    // hosts-file writer) closes off a newline in the payload injecting
    // arbitrary hosts entries, "YouTube.com"/"youtube.com" being treated as
    // different blocked domains, and a pasted full URL blocking nothing.
    public static string? Normalize(string raw)
    {
        var value = raw.Trim();

        var schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            value = value[(schemeIndex + 3)..];
        }

        var cutIndex = value.IndexOfAny(new[] { '/', '?', '#', ':' });
        if (cutIndex >= 0)
        {
            value = value[..cutIndex];
        }

        value = value.ToLowerInvariant();

        return Uri.CheckHostName(value) == UriHostNameType.Dns ? value : null;
    }
}
