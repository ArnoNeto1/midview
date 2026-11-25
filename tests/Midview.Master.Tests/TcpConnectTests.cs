using System.Net.Sockets;
using Midview.Master;
using Xunit;

public class TcpConnectTests
{
    // A device that is off (TEST-NET-1: no machine has this address) runs
    // out of time as a SocketException, like any machine that's off: what
    // every caller already handles. As an OperationCanceledException it
    // slipped past "Salvar a senha de administrador" and closed the Master.
    [Fact]
    public void OutOfTime_IsAMachineThatsOff()
    {
        var ex = Assert.ThrowsAny<SocketException>(() =>
            TcpConnect.WithTimeout("192.0.2.1", 28900, TimeSpan.FromMilliseconds(100)));

        Assert.Equal("Offline", FailureText.Status(ex));
    }

    // Cancelled by whoever asked (a window closing): still a cancellation.
    [Fact]
    public void CancelledByTheCaller_StaysACancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            TcpConnect.WithTimeout("192.0.2.1", 28900, TimeSpan.FromSeconds(5), cancelled.Token));
    }
}
