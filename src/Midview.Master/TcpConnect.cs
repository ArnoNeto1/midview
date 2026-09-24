using System.Net.Sockets;

namespace Midview.Master;

internal static class TcpConnect
{
    // Without a timeout, connecting to a machine that's switched off waits
    // for Windows' own TCP connect timeout (~21 s).
    public static TcpClient WithTimeout(
        string hostAddress, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var tcpClient = new TcpClient();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);
            tcpClient.ConnectAsync(hostAddress, port, linked.Token).AsTask().GetAwaiter().GetResult();
            return tcpClient;
        }
        catch
        {
            tcpClient.Dispose();
            throw;
        }
    }
}
