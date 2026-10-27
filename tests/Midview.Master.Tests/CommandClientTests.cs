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

        var shown = await OwnThread(() => CommandClient.SendCommand(
            "127.0.0.1", session.Port, sessionCertificate.Thumbprint, masterCertificate,
            MessageType.ShowMessage, TextMessage.Encode("Intervalo de 10 minutos")))
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(shown);
        Assert.Equal(new[] { "Intervalo de 10 minutos" }, display.Shown);
    }

    [Fact]
    public async Task CloseApps_ToARunningSession_ReportsWhatWasDone()
    {
        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");
        using var sessionCertificate = SelfSignedCertificate.Create("session-under-test");
        var closer = new CountingCloser();
        using var session = new ThumbnailPollerTests.LoopbackSession(
            0, sessionCertificate, masterCertificate, appCloser: closer);

        var (succeeded, detail) = await OwnThread(() => CommandClient.SendCommandForResult(
            "127.0.0.1", session.Port, sessionCertificate.Thumbprint, masterCertificate,
            MessageType.CloseApps, CloseAppsCommand.Encode(CloseAppsMode.Force, delaySeconds: 0)))
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(succeeded);
        Assert.Equal("pedido enviado a 2 janela(s); 1 programa(s) encerrado(s) à força", detail);
        Assert.Equal(1, closer.ForcedCalls);
    }

    [Fact]
    public async Task NothingListening_FailsInsteadOfHanging()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");

        var send = OwnThread(() => CommandClient.SendCommand(
            "127.0.0.1", port, "ANY", masterCertificate, MessageType.ShowMessage, TextMessage.Encode("Olá")));

        await Assert.ThrowsAsync<SocketException>(() => send.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    // A command blocks on the network, on a thread of its own: its connect
    // completes on a pool thread, which a busy test run can be slow to give
    // without this one holding another meanwhile.
    private static Task<T> OwnThread<T>(Func<T> send) =>
        Task.Factory.StartNew(send, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private sealed class CountingCloser : IAppCloser
    {
        private int _forcedCalls;

        public int ForcedCalls => Volatile.Read(ref _forcedCalls);

        public (int Asked, int Forced) CloseAll(bool force)
        {
            if (force)
            {
                Interlocked.Increment(ref _forcedCalls);
            }
            return (2, force ? 1 : 0);
        }
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

        public void CloseAll()
        {
        }
    }
}
