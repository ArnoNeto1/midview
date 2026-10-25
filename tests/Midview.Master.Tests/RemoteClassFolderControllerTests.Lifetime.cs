using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// RemoteClassFolderController's loop and requests over its life (Ruling 22's
// Task 7a, items 10, 11 and 14): started and stopped at once it leaves no
// failed loop behind; disposed, nothing waiting its turn is left waiting; and
// a home whose handshake hangs costs a poll one wait, not three.
public sealed partial class RemoteClassFolderControllerTests
{
    // Item 10: Start, then Stop before the loop got going: the loop finds its
    // token stopped, rather than failing on a token source already disposed
    // (an exception nobody sees).
    [Fact]
    public void StartedAndStoppedAtOnce_LeavesNoFailedLoop()
    {
        var certificates = Certificates.Shared;
        using var host = Open(certificates.Home, new ScriptedHome(), Trusting(certificates.Colleague));
        var failed = 0;
        void Seen(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.InnerExceptions.Any(ex => ex is ObjectDisposedException && ex.Message.Contains("CancellationTokenSource")))
            {
                Interlocked.Increment(ref failed);
            }
        }
        TaskScheduler.UnobservedTaskException += Seen;
        try
        {
            for (var round = 0; round < 200; round++)
            {
                using var remote = certificates.Remote(host, TimeSpan.FromHours(1));
                remote.Start();
                remote.Stop();
            }
            Thread.Sleep(200); // the loops queued meanwhile have run
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.Equal(0, Volatile.Read(ref failed));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Seen;
        }
    }

    // Item 11: disposed while a command and a state read wait their turn (the
    // window closing in the middle of a click): both end at once, the command
    // refused as disposed; the command already at the home still gets its reply.
    [Fact]
    public async Task Disposed_WhatWaitsItsTurnEndsAtOnce()
    {
        var certificates = Certificates.Shared;
        var scripted = new ScriptedHome { Hold = TimeSpan.FromSeconds(2) };
        using var host = Open(certificates.Home, scripted, Trusting(certificates.Colleague));
        var remote = certificates.Remote(host);
        var first = remote.CreateAsync("9º A");
        var reading = remote.PollAsync();
        await Until(() => Task.FromResult(scripted.MostCommandsAtOnce > 0 && scripted.Reads > 0));
        var second = remote.CreateAsync("9º B");
        var secondRead = remote.PollAsync();

        remote.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
        await secondRead.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await first.WaitAsync(Wait)).Ok);
        await reading.WaitAsync(Wait);
    }

    // Item 14: a home that takes the connection and never answers the
    // handshake (its Master hung) is tried once per poll; a connection it
    // turns away at once is still tried again (its cap on one PC's connections).
    [Fact]
    public async Task AHungHandshake_IsTriedOncePerPoll()
    {
        var certificates = Certificates.Shared;
        var hung = new TcpListener(IPAddress.Loopback, 0);
        hung.Start();
        var accepted = new List<TcpClient>();
        var accepting = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var client = await hung.AcceptTcpClientAsync();
                    lock (accepted)
                    {
                        accepted.Add(client);
                    }
                }
            }
            catch (Exception)
            {
                // Stopped.
            }
        });
        var port = ((IPEndPoint)hung.LocalEndpoint).Port;
        using var remote = new RemoteClassFolderController(
            certificates.Colleague, () => new ClassHome(certificates.Home.Thumbprint, "PROF-1", IPAddress.Loopback.ToString(), port, 1),
            () => Array.Empty<KnownMachine>(), _ => false, TimeSpan.FromHours(1))
        {
            StateWait = TimeSpan.FromSeconds(1),
        };
        try
        {
            await remote.PollAsync();
            await Task.Delay(500); // a retry would have connected by now

            lock (accepted)
            {
                Assert.Single(accepted);
            }
            Assert.False(remote.HomeClosed);
        }
        finally
        {
            hung.Stop();
            await accepting;
            lock (accepted)
            {
                accepted.ForEach(client => client.Dispose());
            }
        }
    }
}
