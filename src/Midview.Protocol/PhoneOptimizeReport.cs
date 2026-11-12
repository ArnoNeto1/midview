using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midview.Protocol;

// How the ad-free DNS stands on the phone (PhoneOptimizeReport.Dns).
public static class PhoneDnsStates
{
    public const string On = "on";
    public const string OffNoInternet = "offNoInternet";
    public const string OffNetworkBlocks = "offNetworkBlocks";
    public const string OffInternetBlocked = "offInternetBlocked";
    public const string OffError = "offError";

    // Turned off in the "Painel do laboratório" ("Bloqueador de anúncios"
    // desativado): "Otimizar" leaves it off until the panel turns it on.
    public const string OffByTeacher = "offByTeacher";

    public static bool IsKnown(string? state) =>
        state is On or OffNoInternet or OffNetworkBlocks or OffInternetBlocked or OffError or OffByTeacher;
}

// Payload of MessageType.PhoneOptimizeReport (UTF-8 JSON): when the phone was
// last optimized (null if never), the packages hidden and those that could
// not be, the DNS state (PhoneDnsStates) and the DNS server's host name.
public sealed record PhoneOptimizeReport(
    [property: JsonPropertyName("optimizedAt")] DateTimeOffset? OptimizedAt,
    [property: JsonPropertyName("hidden")] IReadOnlyList<string> Hidden,
    [property: JsonPropertyName("failed")] IReadOnlyList<string> Failed,
    [property: JsonPropertyName("dns")] string Dns,
    [property: JsonPropertyName("dnsHost")] string DnsHost)
{
    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this, Options);

    public static bool TryDecode(byte[] payload, out PhoneOptimizeReport report)
    {
        report = new PhoneOptimizeReport(null, Array.Empty<string>(), Array.Empty<string>(), PhoneDnsStates.OffError, string.Empty);
        try
        {
            var decoded = JsonSerializer.Deserialize<PhoneOptimizeReport>(
                new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(payload), Options);
            if (decoded is null || decoded.Hidden is null || decoded.Failed is null || decoded.DnsHost is null
                || !PhoneDnsStates.IsKnown(decoded.Dns)
                || decoded.Hidden.Any(p => p is null) || decoded.Failed.Any(p => p is null))
            {
                return false;
            }
            report = decoded;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or NotSupportedException)
        {
            return false;
        }
    }
}
