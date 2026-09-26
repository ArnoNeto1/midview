using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midview.Protocol;

// Payload of MessageType.UpdateStatus (UTF-8 JSON): the installed build and
// the last update attempt. Fingerprint is the installed build's
// PackageFingerprint in hex, null while the Agent is still computing it
// (Ready false) or when the build has no manifest; Build is its label.
public sealed record UpdateStatusReport(
    [property: JsonPropertyName("fingerprint")] string? Fingerprint,
    [property: JsonPropertyName("build")] string? Build,
    [property: JsonPropertyName("ready")] bool Ready,
    [property: JsonPropertyName("attempt")] UpdateAttemptReport? Attempt)
{
    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this, Options);

    public static bool TryDecode(byte[] payload, out UpdateStatusReport report)
    {
        report = new UpdateStatusReport(null, null, false, null);
        try
        {
            var decoded = JsonSerializer.Deserialize<UpdateStatusReport>(
                new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(payload), Options);
            if (decoded is null)
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

// One attempt as the Agent recorded it (see UpdateStates). Id is the
// attempt id in hex, as the Master generated it.
public sealed record UpdateAttemptReport(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);
