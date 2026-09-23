using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Midview.Protocol;

// Local IPC between MidviewSession and MidviewAgent for the input lock.
// Disabling the keyboard/mouse devices needs administrator rights, which
// Session (running as the logged-in student, a standard user) doesn't have
// -- so Session asks the Agent (SYSTEM) to do it. One request byte, one
// reply byte, over a connection Session keeps open for its whole lifetime:
// the Agent re-enables the devices as soon as that connection closes, so a
// crashed or killed Session can never leave a machine's input locked.
public static class InputLockPipe
{
    public const string PipeName = "MidviewInputLock";

    public const byte UnlockRequest = 0;
    public const byte LockRequest = 1;

    public const byte Failure = 0;
    public const byte Success = 1;
}

public sealed class InputLockPipeClient : IDisposable
{
    private const int ConnectTimeoutMs = 2000;

    // Disabling every keyboard and mouse through SetupAPI can take a few seconds.
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(15);

    private readonly string _pipeName;
    private readonly object _gate = new();
    private NamedPipeClientStream? _pipe;

    public InputLockPipeClient(string pipeName = InputLockPipe.PipeName)
    {
        _pipeName = pipeName;
    }

    public bool RequestLock() => Send(InputLockPipe.LockRequest);

    public bool RequestUnlock() => Send(InputLockPipe.UnlockRequest);

    private bool Send(byte request)
    {
        lock (_gate)
        {
            // A connection left over from before an Agent restart is already
            // dead but only fails on first use, so a failure on an existing
            // connection gets one retry on a fresh one.
            var attempts = _pipe is null ? 1 : 2;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    _pipe ??= Connect();
                    _pipe.WriteByte(request);
                    _pipe.Flush();
                    return ReadReply(_pipe) == InputLockPipe.Success;
                }
                catch (Exception ex) when (ex is IOException or TimeoutException
                                               or OperationCanceledException or UnauthorizedAccessException)
                {
                    _pipe?.Dispose();
                    _pipe = null;
                }
            }
            return false;
        }
    }

    private NamedPipeClientStream Connect()
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(ConnectTimeoutMs);

            // Any local process can create a pipe with this name while the
            // Agent isn't running (e.g. during a restart) and pretend to lock.
            // Only services run in session 0, so a server anywhere else can't
            // be the Agent.
            if (OperatingSystem.IsWindows() && !IsServedFromSessionZero(pipe.SafePipeHandle))
            {
                throw new UnauthorizedAccessException("The input-lock pipe isn't served from session 0, so it isn't the Agent");
            }

            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    private static int ReadReply(NamedPipeClientStream pipe)
    {
        var buffer = new byte[1];
        using var timeout = new CancellationTokenSource(ReplyTimeout);
        var read = pipe.ReadAsync(buffer, timeout.Token).AsTask().GetAwaiter().GetResult();
        if (read == 0)
        {
            throw new EndOfStreamException("The Agent closed the input-lock connection");
        }
        return buffer[0];
    }

    private static bool IsServedFromSessionZero(SafePipeHandle pipe) =>
        GetNamedPipeServerSessionId(pipe, out var sessionId) && sessionId == 0;

    public void Dispose()
    {
        lock (_gate)
        {
            _pipe?.Dispose();
            _pipe = null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out uint serverSessionId);
}
