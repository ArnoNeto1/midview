using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Midview.Protocol;

namespace Midview.Agent;

// Serves Session's input-lock requests (see InputLockPipe) over a local
// named pipe, one connection per Session process.
public sealed class InputLockWorker : BackgroundService
{
    private readonly InputLockCoordinator _coordinator;

    public InputLockWorker(InputLockCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // re-enabling devices can take seconds; keep it off the host's startup path

        // The Agent starts at boot, before anyone can log in. Disabling a
        // device persists across reboots, so a power cut or an Agent crash in
        // the middle of an input lock would otherwise leave this machine with
        // no keyboard or mouse at the logon screen -- where no Session is
        // running yet to re-enable them.
        ReleaseAll("at startup");

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                AgentLog.Warn($"Input-lock pipe failed to accept a connection: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            var connectedPipe = pipe;
            _ = Task.Run(() => ServeAndDispose(connectedPipe));
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        ReleaseAll("while stopping"); // a stopped Agent can't release anything later
    }

    private void ServeAndDispose(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
            try
            {
                InputLockConnection.Serve(pipe, _coordinator);
            }
            catch (Exception ex)
            {
                AgentLog.Warn($"Input-lock connection ended with an error: {ex.Message}");
            }
        }
    }

    private void ReleaseAll(string when)
    {
        try
        {
            if (!_coordinator.ReleaseAll())
            {
                AgentLog.Warn($"Failed to re-enable some keyboard/mouse devices {when}.");
            }
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Failed to re-enable keyboard/mouse devices {when}: {ex.Message}");
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        // Session runs as whichever student is logged in. ReadWrite leaves
        // out CreateNewInstance on purpose, so no user process can add its
        // own instance of this pipe and intercept Session's requests.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            InputLockPipe.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, inBufferSize: 0, outBufferSize: 0, security);
    }
}
