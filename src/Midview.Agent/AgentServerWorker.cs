using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Midview.Protocol;

namespace Midview.Agent;

public sealed class AgentServerWorker : BackgroundService
{
    public const int AgentPort = 28900;
    public const int SessionPort = 5901;

    private const string AgentInboundRuleName = "MidviewAgentInbound";
    private const string SessionInboundRuleName = "MidviewSessionInbound";
    private const string DiscoveryInboundRuleName = "MidviewAgentDiscovery";

    // The inbound openings added on every start; the uninstaller removes them.
    public static readonly IReadOnlyList<string> InboundRuleNames =
        new[] { AgentInboundRuleName, SessionInboundRuleName, DiscoveryInboundRuleName };

    // The teachers this machine trusts; pairing adds to it (see PairingService).
    public static string TrustedMastersPath => Path.Combine(AgentDataDirectory.DirectoryPath, "trusted-masters.json");

    public static string SiteBlockStatePath => Path.Combine(AgentDataDirectory.DirectoryPath, "site-block-state.json");

    public static string HostsFilePath => Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\drivers\etc\hosts");

    // A program holding port 28900 while the service was stopped (a
    // student's, during an update) mustn't lock the teacher out until the
    // next restart: the listener keeps trying.
    private static readonly TimeSpan ListenerRetryDelay = TimeSpan.FromSeconds(5);

    private readonly ScheduledShutdown _scheduledShutdown;
    private readonly PowerCommandHandler _powerHandler;
    private readonly UpdateReceiver _updateReceiver;
    private readonly AppBlockCommandHandler _appBlock;
    private readonly TrustStore _masterTrustStore;
    private readonly PairingService _pairing;

    public AgentServerWorker(
        ScheduledShutdown scheduledShutdown, PowerCommandHandler powerHandler, UpdateReceiver updateReceiver,
        AppBlockCommandHandler appBlock, TrustStore masterTrustStore, PairingService pairing)
    {
        _masterTrustStore = masterTrustStore;
        _pairing = pairing;
        _appBlock = appBlock;
        _scheduledShutdown = scheduledShutdown;
        _powerHandler = powerHandler;
        _updateReceiver = updateReceiver;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var certificate = AgentIdentity.LoadOrCreate(AgentIdentity.DefaultCertificatePath);

            EnsureInboundFirewallRuleExists(AgentInboundRuleName, AgentPort);
            // Session can't add its own rule: it runs as a standard user, and
            // Windows' "allow access?" prompt needs an administrator.
            EnsureInboundFirewallRuleExists(SessionInboundRuleName, SessionPort);
            EnsureInboundFirewallRuleExists(DiscoveryInboundRuleName, Discovery.Port, "UDP");

            var statePath = SiteBlockStatePath;
            var state = LoadStateOrStartEmpty(statePath);
            var hostsFilePath = HostsFilePath;
            var firewallController = new NetshFirewallController(AgentPort, SessionPort);
            var commandHandler = new SiteBlockCommandHandler(state, statePath, hostsFilePath, firewallController);

            // Isolated from the listener startup below: a hosts-file error here
            // (e.g. a locked file, antivirus scanning it at boot) must not stop
            // the Agent from listening at all -- that would lock the teacher out
            // of sending even an unblock command until the next restart.
            try
            {
                if (!commandHandler.ReconcileAtStartup())
                {
                    AgentLog.Warn("Failed to reapply the full-internet-block firewall rules at startup — the persisted state says it should be on, but the firewall reconcile failed.");
                }
            }
            catch (Exception ex)
            {
                AgentLog.Warn($"Failed to reconcile site-block state at startup: {ex.Message}");
            }

            return Task.Run(async () =>
            {
                var failing = false;
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var server = new AgentServer(
                            new TcpListener(IPAddress.Any, AgentPort), certificate, _masterTrustStore, commandHandler,
                            _powerHandler, scheduledShutdown: _scheduledShutdown, updateReceiver: _updateReceiver,
                            appBlock: _appBlock, pairing: _pairing);
                        if (failing)
                        {
                            AgentLog.Warn("AgentServer: listening again.");
                            failing = false;
                        }
                        server.Run(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        if (!failing)
                        {
                            AgentLog.Warn($"AgentServer.Run failed, retrying every {ListenerRetryDelay.TotalSeconds} s: {ex.Message}");
                            failing = true;
                        }
                    }

                    try
                    {
                        await Task.Delay(ListenerRetryDelay, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Failed to start the site-blocking listener: {ex.Message}");
            return Task.CompletedTask;
        }
    }

    // Same reasoning as the reconcile isolation above: state that can't be
    // read (a corrupt file is already handled inside Load; this covers I/O
    // errors like a locked file) must not keep the listener from starting.
    // Nothing blocked is the direction the teacher can always fix remotely.
    private static SiteBlockState LoadStateOrStartEmpty(string statePath)
    {
        try
        {
            return SiteBlockState.Load(statePath);
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Failed to load site-block state, starting with nothing blocked: {ex.Message}");
            return new SiteBlockState();
        }
    }

    private static void EnsureInboundFirewallRuleExists(string ruleName, int port, string protocol = "TCP")
    {
        // "add rule" never replaces a rule with the same name, so it's deleted
        // first -- otherwise every Agent start would add one more copy.
        RunNetsh("advfirewall", "firewall", "delete", "rule", $"name={ruleName}");
        RunNetsh("advfirewall", "firewall", "add", "rule",
            $"name={ruleName}", "dir=in", "action=allow",
            $"protocol={protocol}", $"localport={port}");
    }

    internal static void RunNetsh(params string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("netsh.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }
        using var process = System.Diagnostics.Process.Start(startInfo);
        process?.WaitForExit();
    }
}
