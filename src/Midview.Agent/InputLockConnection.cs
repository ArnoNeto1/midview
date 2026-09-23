using Midview.Protocol;

namespace Midview.Agent;

public static class InputLockConnection
{
    // Serves one Session's connection until it closes. Whatever that Session
    // held is released on the way out, however the connection ended -- this
    // is what guarantees a crashed or killed Session can't leave the
    // keyboard and mouse disabled.
    public static void Serve(Stream stream, InputLockCoordinator coordinator)
    {
        var holder = new object();
        try
        {
            while (true)
            {
                var request = stream.ReadByte();
                if (request < 0)
                {
                    return;
                }

                var succeeded = request switch
                {
                    InputLockPipe.LockRequest => coordinator.Acquire(holder),
                    InputLockPipe.UnlockRequest => coordinator.Release(holder),
                    _ => false,
                };
                stream.WriteByte(succeeded ? InputLockPipe.Success : InputLockPipe.Failure);
                stream.Flush();
            }
        }
        finally
        {
            coordinator.Release(holder);
        }
    }
}
