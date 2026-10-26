using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// RemoteClassFolderController's loop and requests over its life: started and
// stopped at once it leaves no
// failed loop behind; disposed, nothing waiting its turn is left waiting; and
// a home whose handshake hangs costs a poll one wait, not three.
public sealed partial class RemoteClassFolderControllerTests
{
    // Start, then Stop before the loop got going: the loop finds its
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

    // Disposed while a command and a state read wait their turn (the
    // window closing in the middle of a click): both end at once, the command
    // refused as disposed; the command already at the home still gets its reply.
    // At once: while the home still holds the first command and read, so
    // never after their turn came, however long a busy test run takes.
    [Fact]
    public async Task Disposed_WhatWaitsItsTurnEndsAtOnce()
    {
        var certificates = Certificates.Shared;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scripted = new ScriptedHome { Gate = held.Task };
        using var host = Open(certificates.Home, scripted, Trusting(certificates.Colleague));
        var remote = certificates.Remote(host);
        try
        {
            var first = remote.CreateAsync("9º A");
            var reading = remote.PollAsync();
            await Until(() => Task.FromResult(scripted.MostCommandsAtOnce > 0 && scripted.Reads > 0));
            var second = remote.CreateAsync("9º B");
            var secondRead = remote.PollAsync();

            remote.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => second.WaitAsync(Wait));
            await secondRead.WaitAsync(Wait);
            Assert.False(first.IsCompleted);
            held.SetResult();
            Assert.True((await first.WaitAsync(Wait)).Ok);
            await reading.WaitAsync(Wait);
        }
        finally
        {
            // A failed assertion above must not leave the home's handler threads held.
            held.TrySetResult();
            remote.Dispose();
        }
    }

    // A home that takes the connection and never answers the
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
        int AcceptedCount()
        {
            lock (accepted)
            {
                return accepted.Count;
            }
        }
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
            // The accept loop runs on the pool, which a busy test run can hold back:
            // the first connection is counted first, then a retry would have had time.
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (AcceptedCount() == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
            await Task.Delay(500);

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
