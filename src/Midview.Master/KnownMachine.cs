namespace Midview.Master;

// One machine in the teacher's classroom list. AgentThumbprint is optional:
// it's only needed for site blocking, and whatever the teacher typed in that
// field when saving is kept as-is.
public sealed record KnownMachine(string Name, string HostAddress, string SessionThumbprint, string? AgentThumbprint);
