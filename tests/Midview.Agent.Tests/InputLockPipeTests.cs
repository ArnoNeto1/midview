using System.IO.Pipes;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// End to end over a real named pipe: Session's InputLockPipeClient against
// the Agent's InputLockConnection. (The Agent's pipe ACL and the client's
// session-0 check are Windows-only and aren't exercised here.)
public class InputLockPipeTests
{
    [Fact]
    public async Task RequestLockThenUnlock_DisablesThenReEnablesDevices()
    {
        var pipeName = UniquePipeName();
        var devices = new FakeInputDeviceController();
        var serverTask = ServeOneConnection(pipeName, new InputLockCoordinator(devices));
        using var client = new InputLockPipeClient(pipeName);

        Assert.True(client.RequestLock());
        Assert.Equal(1, devices.DisableCalls);

        Assert.True(client.RequestUnlock());
        Assert.Equal(1, devices.EnableCalls);

        client.Dispose();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ClientGoesAwayWhileLocked_AgentReEnablesDevices()
    {
        var pipeName = UniquePipeName();
        var devices = new FakeInputDeviceController();
        var serverTask = ServeOneConnection(pipeName, new InputLockCoordinator(devices));
        var client = new InputLockPipeClient(pipeName);
        Assert.True(client.RequestLock());

        client.Dispose(); // what a crashed or killed Session looks like from the Agent's side

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, devices.EnableCalls);
    }

    [Fact]
    public void AgentNotRunning_RequestLockFailsInsteadOfHanging()
    {
        using var client = new InputLockPipeClient(UniquePipeName());

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(client.RequestLock());

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AgentRestartedBetweenRequests_ClientReconnectsOnItsOwn()
    {
        var pipeName = UniquePipeName();
        var devices = new FakeInputDeviceController();
        var coordinator = new InputLockCoordinator(devices);
        using var client = new InputLockPipeClient(pipeName);

        var firstServer = ServeOneConnection(pipeName, coordinator, closeAfterRequests: 1);
        Assert.True(client.RequestLock());
        await firstServer.WaitAsync(TimeSpan.FromSeconds(5));

        _ = ServeOneConnection(pipeName, coordinator);
        Assert.True(client.RequestLock());
        Assert.Equal(2, devices.DisableCalls); // the first connection's lock was released when it closed
    }

    private static string UniquePipeName() => $"midview-test-{Guid.NewGuid():N}";

    // Created before returning so the client never races the server's
    // creation; served on a background task like the Agent does.
    private static Task ServeOneConnection(
        string pipeName, InputLockCoordinator coordinator, int? closeAfterRequests = null)
    {
        var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        return Task.Run(async () =>
        {
            using (server)
            {
                await server.WaitForConnectionAsync();
                Stream stream = closeAfterRequests is int limit ? new RequestLimitedStream(server, limit) : server;
                InputLockConnection.Serve(stream, coordinator);
            }
        });
    }

    // Reports end-of-stream after a set number of requests, standing in for
    // an Agent that stops right after answering.
    private sealed class RequestLimitedStream : Stream
    {
        private readonly Stream _inner;
        private int _requestsLeft;

        public RequestLimitedStream(Stream inner, int requests)
        {
            _inner = inner;
            _requestsLeft = requests;
        }

        public override int ReadByte() => _requestsLeft-- > 0 ? _inner.ReadByte() : -1;
        public override void WriteByte(byte value) => _inner.WriteByte(value);
        public override void Flush() => _inner.Flush();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    }
}
