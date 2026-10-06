using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midview.Protocol;

// Finding the machines with Midview on the local network: the Master
// broadcasts a probe to UDP Port, and every Agent that hears it answers the
// sender. Nothing here is trusted: a reply only tells the teacher what is
// there. Identity comes from pairing, or from thumbprints already pinned in
// the classroom list. One reply per probe, about the probe's size, so it
// can't amplify traffic.
public static class Discovery
{
    public const int Port = 28901;
    public const int MaxPacketSize = 2048;
    public const int Version = 1;

    // How long the Master listens for replies. Windows lets unicast
    // replies to a broadcast in for 3 seconds by default.
    public static readonly TimeSpan ListenTime = TimeSpan.FromSeconds(2);

    public static byte[] Encode(DiscoveryProbe probe) =>
        JsonSerializer.SerializeToUtf8Bytes(new ProbeJson(Version, probe.Id, probe.MasterThumbprint));

    public static byte[] Encode(DiscoveryReply reply) =>
        JsonSerializer.SerializeToUtf8Bytes(new ReplyJson(
            Version, reply.ProbeId, reply.Name, reply.AgentThumbprint, reply.Teachers, reply.NewTeacherOpen));

    // A reply carries "agent", which a probe never does.
    public static DiscoveryProbe? ParseProbe(byte[] packet) =>
        Read<ProbeJson>(packet) is { Midview: Version, Master: { Length: > 0 } master, Agent: null } json && json.Probe != Guid.Empty
            ? new DiscoveryProbe(json.Probe, master)
            : null;

    public static DiscoveryReply? ParseReply(byte[] packet) =>
        Read<ReplyJson>(packet) is { Midview: Version, Name: { Length: > 0 } name, Agent: { Length: > 0 } agent, Teachers: { } teachers } json
        && json.Probe != Guid.Empty && TeacherStatus.IsKnown(teachers)
            ? new DiscoveryReply(json.Probe, name, agent, teachers, json.NewTeacherOpen)
            : null;

    private static T? Read<T>(byte[] packet) where T : class
    {
        if (packet.Length == 0 || packet.Length > MaxPacketSize)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(packet);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ProbeJson(
        [property: JsonPropertyName("midview")] int Midview,
        [property: JsonPropertyName("probe")] Guid Probe,
        [property: JsonPropertyName("master")] string? Master,
        [property: JsonPropertyName("agent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Agent = null);

    private sealed record ReplyJson(
        [property: JsonPropertyName("midview")] int Midview,
        [property: JsonPropertyName("probe")] Guid Probe,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("agent")] string? Agent,
        [property: JsonPropertyName("teachers")] string? Teachers,
        [property: JsonPropertyName("newTeacherOpen")] bool NewTeacherOpen);
}

public sealed record DiscoveryProbe(Guid Id, string MasterThumbprint);

public sealed record DiscoveryReply(Guid ProbeId, string Name, string AgentThumbprint, string Teachers, bool NewTeacherOpen);

// DiscoveryReply.Teachers: whom the machine trusts, as the asking teacher
// sees it.
public static class TeacherStatus
{
    public const string None = "none";
    public const string You = "you";
    public const string Others = "others";

    public static bool IsKnown(string value) => value is None or You or Others;
}
