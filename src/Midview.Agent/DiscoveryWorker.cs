using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Midview.Protocol;

namespace Midview.Agent;

// Runs the DiscoveryResponder on UDP Discovery.Port for as long as the
// service runs; a port that can't be opened is retried, as AgentServer's.
public sealed class DiscoveryWorker : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    // Without this, Windows ends a UDP socket's next receive with an error
    // when an earlier send got "port unreachable" back.
    private const int SioUdpConnReset = -1744830452;

    private readonly PairingService _pairing;

    public DiscoveryWorker(PairingService pairing)
    {
        _pairing = pairing;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string agentThumbprint;
        try
        {
            using var certificate = AgentIdentity.LoadOrCreate(AgentIdentity.DefaultCertificatePath);
            agentThumbprint = certificate.Thumbprint;
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Discovery: no Agent identity, not answering: {ex.Message}");
            return;
        }

        var responder = new DiscoveryResponder(_pairing, agentThumbprint, Environment.MachineName);
        var failing = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, Discovery.Port));
                udp.Client.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
                if (failing)
                {
                    AgentLog.Warn("Discovery: answering again.");
                    failing = false;
                }
                await responder.RunAsync(udp, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!failing)
                {
                    AgentLog.Warn($"Discovery failed, retrying every {RetryDelay.TotalSeconds} s: {ex.Message}");
                    failing = true;
                }
            }

            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
