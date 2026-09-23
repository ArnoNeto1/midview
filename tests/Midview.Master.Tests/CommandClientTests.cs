using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class CommandClientTests
{
    [Fact]
    public async Task ShowMessage_ToARunningSession_IsShownAndAcknowledged()
    {
        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");
        using var sessionCertificate = SelfSignedCertificate.Create("session-under-test");
        var display = new Recorder();
        using var session = new ThumbnailPollerTests.LoopbackSession(0, sessionCertificate, masterCertificate, display);

        var shown = await Task.Run(() => CommandClient.SendCommand(
            "127.0.0.1", session.Port, sessionCertificate.Thumbprint, masterCertificate,
            MessageType.ShowMessage, TeacherMessage.Encode("Intervalo de 10 minutos")))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(shown);
        Assert.Equal(new[] { "Intervalo de 10 minutos" }, display.Shown);
    }

    [Fact]
    public async Task NothingListening_FailsInsteadOfHanging()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");

        var send = Task.Run(() => CommandClient.SendCommand(
            "127.0.0.1", port, "ANY", masterCertificate, MessageType.ShowMessage, TeacherMessage.Encode("Olá")));

        await Assert.ThrowsAsync<SocketException>(() => send.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private sealed class Recorder : IMessageDisplay
    {
        public List<string> Shown { get; } = new();

        public void Show(string text)
        {
            lock (Shown)
            {
                Shown.Add(text);
            }
        }
    }
}
