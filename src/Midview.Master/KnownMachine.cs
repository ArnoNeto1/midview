namespace Midview.Master;

// One machine in the teacher's classroom list. AgentThumbprint is optional:
// a machine can be saved for screen viewing before its Agent's thumbprint
// is known (site-blocking just won't work for it until it's filled in).
public sealed record KnownMachine(string Name, string HostAddress, string SessionThumbprint, string? AgentThumbprint);
