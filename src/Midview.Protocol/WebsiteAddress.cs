using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Midview.Protocol;

// What OpenWebsite carries, and what the teacher may type for it: a
// website's address and nothing else Windows would open -- no files, no
// programs, no other kinds of links (mailto:, ms-settings:...). Typed
// without http:// or https://, it's taken as https://, as a browser would.
// The same check on both ends: the Master says what's wrong with what the
// teacher typed, the Session refuses anything else.
public static class WebsiteAddress
{
    public const int MaxLength = TextMessage.MaxLength;

    private const string NotAWebsite =
        "Só endereços de sites (http:// ou https://), como www.google.com. Arquivos, programas e outros tipos de link não abrem.";

    private const string NotAnAddress = "Isso não parece o endereço de um site. Escreva-o como www.google.com.";

    // "something:" at the start, unless what follows is a port ("localhost:3000").
    private static readonly Regex Scheme = new(
        @"^([a-z][a-z0-9+.\-]*):(?!\d+(?:[/?#]|$))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryParse(string? typed, [NotNullWhen(true)] out Uri? address, out string problem)
    {
        address = null;
        var text = typed?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            problem = "Escreva o endereço do site, como www.google.com.";
            return false;
        }
        if (text.Length > MaxLength)
        {
            problem = "Endereço longo demais.";
            return false;
        }

        var scheme = Scheme.Match(text);
        if (text.Contains('\\') || text.StartsWith('/') || (scheme.Success && !IsWebScheme(scheme.Groups[1].Value)))
        {
            problem = NotAWebsite;
            return false;
        }

        // Without a scheme, a name with no dot ("google", "notepad") is more
        // likely a search or a program than a website; "localhost" aside.
        if (text.Any(char.IsWhiteSpace)
            || !Uri.TryCreate(scheme.Success ? text : "https://" + text, UriKind.Absolute, out var uri)
            || !IsWebScheme(uri.Scheme)
            || uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6)
            || (!scheme.Success && uri.HostNameType == UriHostNameType.Dns
                && !uri.Host.Contains('.') && uri.Host != "localhost"))
        {
            problem = NotAnAddress;
            return false;
        }

        // "https://" and escaped characters make it longer than typed.
        if (uri.AbsoluteUri.Length > MaxLength)
        {
            problem = "Endereço longo demais.";
            return false;
        }

        address = uri;
        problem = string.Empty;
        return true;
    }

    private static bool IsWebScheme(string scheme) =>
        scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
