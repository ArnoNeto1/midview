using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHost: the connections themselves (clients that misbehave, the
// deadline, the caps, a port another program holds, closing the host, a
// command that takes too long). What each request gets is in ClassFolderHostTests.cs.
public sealed partial class ClassFolderHostTests
{
    // Nonsense, a client that hangs up, a handshake never finished: the next colleague is still served.
    [Fact]
    public async Task TheHost_OutlivesClientsThatSendNonsense()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        using var host = Open(rig.Master, controller, Trusting(rig.OtherMaster));

        using (var garbage = new TcpClient())
        {
            garbage.Connect(IPAddress.Loopback, host.Port);
            garbage.GetStream().Write(Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\n\r\n"));
        }
        using (var silent = new TcpClient())
        {
            silent.Connect(IPAddress.Loopback, host.Port);
        }
        Assert.ThrowsAny<Exception>(() =>
        {
            using var tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, host.Port);
            tcp.ReceiveTimeout = 10_000;
            using var channel = SecureChannel.AuthenticateAsClient(tcp, "localhost", rig.OtherMaster, new TrustStore(new[] { "00" }));
        });

        // As soon as the host has let go of them (one PC gets a few connections at once).
        Assert.Equal(2, (await StateSoon(host, rig.OtherMaster, rig.Master)).Classes.Count);
    }

    // A client trickling a byte now and then never gets past the deadline for
    // the handshake and the request, though no single read waits long; one
    // that sends nothing is dropped too.
    [Fact]
    public async Task ASilentOrTricklingClient_IsDroppedAtTheDeadline()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        using var colleague = SelfSignedCertificate.Create("colleague-under-test");
        using var host = Open(master, new FakeController(), Trusting(colleague));
        host.ReadTimeout = TimeSpan.FromMilliseconds(800);

        var trickling = await Task.Run(() =>
        {
            using var tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, host.Port);
            var stream = tcp.GetStream();
            // A TLS record header announcing 16 KB, then its body a byte at a time.
            stream.Write(new byte[] { 0x16, 0x03, 0x01, 0x3F, 0xFF });
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (clock.Elapsed < Wait)
                {
                    stream.Write(new byte[] { 0x41 });
                    Thread.Sleep(100);
                }
            }
            catch (IOException)
            {
                // Dropped.
            }
            return clock.Elapsed;
        });
        var silent = await Task.Run(() =>
        {
            using var tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, host.Port);
            tcp.ReceiveTimeout = (int)Wait.TotalMilliseconds;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Assert.Equal(0, tcp.GetStream().Read(new byte[16]));
            }
            catch (IOException)
            {
                // Dropped with a reset.
            }
            return clock.Elapsed;
        });

        Assert.True(trickling < TimeSpan.FromSeconds(5), $"trickling client kept {trickling}");
        Assert.True(silent < TimeSpan.FromSeconds(5), $"silent client kept {silent}");
    }

    // A request is a few names: anything over 64 KB isn't read, let alone served.
    [Fact]
    public async Task AnOversizedRequest_IsRefused()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        using var host = Open(rig.Master, controller, Trusting(rig.OtherMaster));
        var huge = ClassFolderRemote.EncodeJson(new RemoteCommand(RemoteCommand.Create, new string('x', 70 * 1024), null, null));

        await Task.Run(() => Assert.ThrowsAny<Exception>(() =>
            Ask(host, rig.OtherMaster, rig.Master, new Message(MessageType.RemoteClassFolderCommand, huge))));

        Assert.Equal(new[] { Turma, "3º B" }, (await StateSoon(host, rig.OtherMaster, rig.Master)).Classes.Select(c => c.Turma));
    }

    // One PC can't take every connection: a fourth at once is closed unanswered,
    // and served again once the others are gone.
    [Fact]
    public async Task OnePc_HoldsAtMostThreeConnectionsAtOnce()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        using var colleague = SelfSignedCertificate.Create("colleague-under-test");
        var controller = new FakeController { Read = _ => EmptyState() };
        using var host = Open(master, controller, Trusting(colleague));
        var held = Enumerable.Range(0, 3).Select(_ =>
        {
            var tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, host.Port);
            return tcp;
        }).ToList();
        try
        {
            await Task.Run(() => Assert.ThrowsAny<Exception>(() => State(host, colleague, master)));
        }
        finally
        {
            held.ForEach(tcp => tcp.Dispose());
        }

        Assert.Equal("PROF-1", (await StateSoon(host, colleague, master)).HomeName);
    }

    // Closing the home's Master while a colleague's end is bringing the last
    // files in: no new connection is taken, and the end finishes and is answered.
    [Fact]
    public async Task Disposing_WhileACommandRuns_LetsItFinish()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var sync = rig.NewSync();
        var controller = LocalClassFolderControllerTests.NewController(rig, sync);
        var logged = new ConcurrentQueue<string>();
        var host = ClassFolderHost.Open(rig.Master, controller, Trusting(rig.OtherMaster), 0, logged.Enqueue, IPAddress.Loopback)!;
        await controller.StartAsync(Turma);
        rig.Pcs[0].Write("ultimo.txt", new byte[] { 1, 2, 3 }, Utc(2));
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        sync.CopyingStarts = _ =>
        {
            reached.Set();
            release.Wait(Wait);
        };
        var ending = Task.Run(() => Command(
            host, rig.OtherMaster, rig.Master, new RemoteCommand(RemoteCommand.End, Turma, null, RemoteCommand.Keep)));
        Assert.True(reached.Wait(Wait));

        host.Dispose();
        Assert.ThrowsAny<SocketException>(() =>
        {
            using var tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, host.Port);
        });
        release.Set();
        var ended = await ending.WaitAsync(Wait);

        Assert.True(ended.Ok, ended.Detail);
        Assert.False(controller.Lesson.IsOn);
        Assert.Equal(new[] { "ultimo.txt" }, rig.StoreFiles());
        Assert.Empty(logged);
    }

    [Fact]
    public void APortAnotherProgramHolds_IsLogged_AndGivesNoHost()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        try
        {
            var logged = new List<string>();
            var host = ClassFolderHost.Open(
                rig.Master, controller, Trusting(rig.OtherMaster), ((IPEndPoint)taken.LocalEndpoint).Port, logged.Add, IPAddress.Loopback);

            Assert.Null(host);
            Assert.Single(logged);
        }
        finally
        {
            taken.Stop();
        }
    }

    // Ruling 15: a state read is a poll, answered within about ReadTimeout
    // (the colleague's PC is waiting on it), not the minutes a command may take.
    [Fact]
    public void AStateReadThatHangs_IsAnsweredWithinTheReadTimeout()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        using var colleague = SelfSignedCertificate.Create("colleague-under-test");
        var never = new TaskCompletionSource<RemoteState>();
        using var host = Open(master, new FakeController { Reading = _ => never.Task }, Trusting(colleague));
        host.ReadTimeout = TimeSpan.FromSeconds(1);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var answer = Ask(host, colleague, master, new Message(MessageType.RemoteClassFolderState, Array.Empty<byte>()));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"answered after {clock.Elapsed}"); // not CommandTimeout's minutes
        Assert.Equal(MessageType.CommandAck, answer.Type);
        Assert.Equal((false, ClassFolderRemote.UnreadableText + ClassFolderHost.TooLongReason), CommandResult.Decode(answer.Payload));
    }

    // A command still running when the wait is over is answered so, not left hanging.
    [Fact]
    public void ACommandThatTakesTooLong_IsAnsweredThatItIsStillRunning()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        using var colleague = SelfSignedCertificate.Create("colleague-under-test");
        var never = new TaskCompletionSource<IReadOnlyList<ResultLineDto>>();
        var controller = new FakeController { Start = _ => never.Task };
        using var host = Open(master, controller, Trusting(colleague));
        host.CommandTimeout = TimeSpan.FromMilliseconds(300);

        var reply = Command(host, colleague, master, new RemoteCommand(RemoteCommand.Start, Turma, null, null));

        Assert.Equal((false, ClassFolderHost.StillRunningText, Turma), (reply.Ok, reply.Detail, reply.Turma));
    }
}
