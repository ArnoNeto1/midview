using System.Text.Json.Serialization;
using Midview.Protocol;

namespace Midview.Master;

// One machine in the teacher's classroom list. AgentThumbprint is optional:
// a machine can be saved for screen viewing before its Agent's thumbprint
// is known (site-blocking just won't work for it until it's filled in).
// MacAddress ("AA-BB-CC-DD-EE-FF") is what turns it on over the network
// (MainWindow's "Ligar"); it comes from the installer's machine file or from the
// Agent itself, the first time Master sees the machine on.
public sealed record KnownMachine(
    string Name, string HostAddress, string SessionThumbprint, string? AgentThumbprint, string? MacAddress = null,
    DeviceKind Kind = DeviceKind.Windows)
{
    // An Android phone, not a Windows PC. Not saved: it follows from Kind.
    [JsonIgnore]
    public bool IsPhone => Kind == DeviceKind.Android;

    // For a new version of the same entry (edited by hand, or imported
    // again) that doesn't know the network card: keeps the one known
    // before, as long as the entry still points to the same host.
    public KnownMachine KeepingMacAddressOf(KnownMachine previous) =>
        MacAddress is null && string.Equals(HostAddress, previous.HostAddress, StringComparison.OrdinalIgnoreCase)
            ? this with { MacAddress = previous.MacAddress }
            : this;

    // For the entry edited by hand ("Editar a máquina"), which can't say
    // what kind of device it is: a phone stays a phone.
    public KnownMachine KeepingKindOf(KnownMachine previous) =>
        Kind == DeviceKind.Windows ? this with { Kind = previous.Kind } : this;

    // The same roster entry, whether or not its MAC address was learned in
    // the meantime.
    public bool IsSameEntryAs(KnownMachine? other) =>
        other is not null && this with { MacAddress = null } == other with { MacAddress = null };
}
