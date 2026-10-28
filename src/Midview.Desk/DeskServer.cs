using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;
using Midview.Session;

namespace Midview.Desk;

// The helper's own TLS server on its own port: it streams the screen of
// whichever desktop currently takes the keyboard and mouse -- the user's, then
// the secure (UAC) desktop while a prompt is up, then back -- and injects the
// teacher's input onto it. It serves one trusted Master, over the same wire
// protocol SessionServer uses (ScreenStream/ScreenFrame + FrameCredit for the
// screen, StartControl/StopControl/InputEvent for control, and
// FillAdminPassword to type the admin password into a UAC prompt), trimmed to screen
// and input only: no chat, files, class folder, input lock, thumbnails or
// screen lock (the Session, in its own process, holds the input lock through a
// separate HoldInputLock connection; the helper only sees and drives).
//
// Capture and injection both have to run on a thread attached to the input
// desktop (a display DC and SendInput are desktop-bound), so one worker thread
// does both: each cycle it follows the desktop (attaching and remaking the
// capture when it changes), injects the input the receive loop queued, and
// sends the next screen update. The receive loop runs on its own thread and
// only enqueues input, so injection always lands on the desktop-bound thread.
// Attaching the thread to the desktop is the injected attachThreadToDesktop
// seam: a no-op by default so this class is testable on the normal desktop,
// the real SetThreadDesktop in Task 7.
public sealed class DeskServer
{
    // Plenty for a real LAN handshake plus the first message, and how long a
    // silent connection is kept before its blocked Receive() gives up: a
    // controlling Master heartbeats about once a second, so this much silence
    // means it is gone (powered off, cable pulled) without closing the socket.
    private const int ConnectionTimeoutMs = 15_000;

    private const int CaptureRetryDelayMs = 500;

    // How long the ScreenFrame loop waits for the viewer to acknowledge before
    // giving up on an update and carrying on (see FrameCredit), as SessionServer.
    private static readonly TimeSpan FrameAckTimeout = TimeSpan.FromSeconds(1);

    private readonly TcpListener _listener;
    private readonly X509Certificate2 _agentCertificate;
    private readonly TrustStore _masterTrustStore;
    private readonly Func<IScreenCapture> _captureForCurrentDesktop;
    private readonly IInputInjector _inputInjector;
    private readonly DesktopFollower _follower;
    private readonly Action<IntPtr> _attachThreadToDesktop;

    // One controlling Master served at a time, the same single-serving-slot
    // policy SessionServer uses: Program.cs wires one shared desktop-following
    // state (InputDesktopSource, the attach closure, the follower), which two
    // concurrent workers would race. A second trusted Master is refused while
    // one is being served. Wait(0), never a queue -- a controller must not wait
    // behind another for the UAC desktop.
    private readonly SemaphoreSlim _servingSlot = new(1, 1);

    public DeskServer(
        TcpListener listener,
        X509Certificate2 agentCertificate,
        TrustStore masterTrustStore,
        Func<IScreenCapture> captureForCurrentDesktop,
        IInputInjector inputInjector,
        DesktopFollower follower,
        Action<IntPtr>? attachThreadToDesktop = null)
    {
        _listener = listener;
        _agentCertificate = agentCertificate;
        _masterTrustStore = masterTrustStore;
        _captureForCurrentDesktop = captureForCurrentDesktop;
        _inputInjector = inputInjector;
        _follower = follower;
        // The real SetThreadDesktop is wired in Task 7; a no-op keeps the
        // normal-desktop path (and the tests) working without it.
        _attachThreadToDesktop = attachThreadToDesktop ?? (_ => { });
    }

    public void Run(CancellationToken cancellationToken)
    {
        _listener.Start();
        while (!cancellationToken.IsCancellationRequested)
        {
            var tcpClient = _listener.AcceptTcpClient();
            // Nagle's algorithm batches small writes -- wrong for this
            // protocol's frequent tiny messages (input events, heartbeats,
            // frame acknowledgements), which it would only delay.
            tcpClient.NoDelay = true;
            // Off the accept loop and on a thread of its own, since the handler
            // blocks for as long as the connection lasts: a connection stuck in
            // its TLS handshake must not keep the next one from being accepted.
            _ = Task.Factory.StartNew(
                () => HandleClientSafely(tcpClient, cancellationToken), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    private void HandleClientSafely(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        try
        {
            HandleClient(tcpClient, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling a Desk connection: {ex.Message}");
        }
    }

    private void HandleClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        using var client = tcpClient;
        client.ReceiveTimeout = ConnectionTimeoutMs;
        client.SendTimeout = ConnectionTimeoutMs;
        // The trust store gates the Master during the handshake: a Master whose
        // thumbprint isn't trusted fails it here and gets nothing. The agent
        // certificate is this server's own.
        using var channel = SecureChannel.AuthenticateAsServer(client, _agentCertificate, _masterTrustStore);

        // Past the trust gate: take the single serving slot before touching the
        // shared desktop state. A second trusted Master is refused here rather
        // than queued, so it never starts a second worker over the one shared
        // InputDesktopSource/follower/attach closure.
        if (!_servingSlot.Wait(0))
        {
            Console.WriteLine("Another Master is already being served by the Desk helper; closing the new connection.");
            return;
        }

        try
        {
            Serve(client, channel, cancellationToken);
        }
        finally
        {
            _servingSlot.Release();
        }
    }

    private void Serve(TcpClient tcpClient, SecureChannel channel, CancellationToken cancellationToken)
    {
        var isControlling = false;
        var credit = new FrameCredit();
        var cadence = new CaptureCadence();

        // Input decoded on this (receive) thread and injected on the worker
        // thread, so it lands on the desktop the worker is attached to.
        var pendingInput = new ConcurrentQueue<InputEvent>();

        // Admin passwords (UTF-8) for the UAC prompt, queued here and typed on
        // the worker thread for the same reason as the input above.
        var pendingFills = new ConcurrentQueue<byte[]>();

        // The worker only sends (ScreenFrame), this loop only receives -- the
        // one reader / one writer pattern SslStream supports without locking.
        using var captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var worker = new Thread(() => DesktopWorker(channel, tcpClient, captureCts, credit, cadence, pendingInput, pendingFills))
        {
            IsBackground = true,
        };
        worker.Start();

        try
        {
            while (!cancellationToken.IsCancellationRequested && tcpClient.Connected)
            {
                var message = channel.Receive();
                switch (message.Type)
                {
                    // One gate for the whole connection: a repeated StartControl
                    // holds nothing more, and a StopControl while not controlling
                    // (the Master's window sends it as it closes either way) does
                    // nothing. The input lock is the Session's, not the helper's.
                    case MessageType.StartControl:
                        isControlling = true;
                        break;

                    case MessageType.StopControl:
                        isControlling = false;
                        break;

                    // Only keeps the connection from timing out; nothing to do.
                    case MessageType.Heartbeat:
                        break;

                    case MessageType.InputEvent:
                        if (isControlling)
                        {
                            InputEvent inputEvent;
                            try
                            {
                                inputEvent = InputEventCodec.Decode(message.Payload);
                            }
                            catch (Exception ex)
                            {
                                // InputEventCodec has no length guard, so a
                                // short or malformed payload throws. Drop the
                                // connection rather than let it crash the
                                // server; returning closes the socket below.
                                Console.WriteLine($"Dropping a Desk connection after a malformed input event: {ex.Message}");
                                return;
                            }

                            pendingInput.Enqueue(inputEvent);
                            cadence.Activity(Environment.TickCount64);
                        }
                        break;

                    // The one-click UAC password fill, only while controlling
                    // (ignored otherwise). The helper answers nothing; the
                    // worker types it onto whichever desktop it is attached to.
                    case MessageType.FillAdminPassword:
                        if (isControlling)
                        {
                            if (!CredentialFill.TryDecode(message.Payload, out var passwordUtf8))
                            {
                                // Malformed (empty or over the cap): drop the
                                // connection as a malformed input event does;
                                // returning closes the socket below.
                                Console.WriteLine("Dropping a Desk connection after a malformed password fill.");
                                return;
                            }

                            pendingFills.Enqueue(passwordUtf8);
                            cadence.Activity(Environment.TickCount64);
                        }
                        break;

                    case MessageType.FrameAck:
                        if (ScreenFrame.DecodeAck(message.Payload) is { } frameId)
                        {
                            credit.Acknowledge(frameId, Environment.TickCount64);
                        }
                        break;

                    // Anything else (ScreenStream included) is ignored: the
                    // worker streams the screen regardless of it.
                }
            }
        }
        finally
        {
            captureCts.Cancel();
            worker.Join(TimeSpan.FromSeconds(2));
        }
    }

    // The desktop-bound worker: follow the input desktop, inject the queued
    // input, and stream the next screen update, at FrameCredit's pace and
    // CaptureCadence's rhythm -- the same screen path as
    // SessionServer.StreamScreenFrames.
    private void DesktopWorker(
        SecureChannel channel, TcpClient tcpClient, CancellationTokenSource captureCts,
        FrameCredit credit, CaptureCadence cadence, ConcurrentQueue<InputEvent> pendingInput,
        ConcurrentQueue<byte[]> pendingFills)
    {
        // Each control connection starts its own capture: forget any desktop a
        // previous connection's worker reported, so the first poll below reports
        // the current desktop and makes the capture even when the desktop has not
        // changed since the last session. The serving slot means only this worker
        // uses the shared follower now.
        _follower.Reset();

        FramePipeline? pipeline = null;
        try
        {
            while (credit.WaitForRoom(FrameAckTimeout, captureCts.Token))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Attach this thread to the desktop the follower reports and
                // remake the capture for it, but never attach to a zero handle
                // (no input desktop). On a real machine the first poll already
                // gives the current desktop, so the capture exists before the
                // first frame.
                if (_follower.PollChanged(out var desktop) && desktop != IntPtr.Zero)
                {
                    _attachThreadToDesktop(desktop);
                    // Free the previous pipeline's bitmaps now rather than leave
                    // them to be GC-finalized: this SYSTEM process must not churn
                    // GDI handles on every desktop switch.
                    pipeline?.Dispose();
                    pipeline = new FramePipeline(_captureForCurrentDesktop());
                }

                // Inject here, on the desktop-bound thread, never on the receive
                // thread, which isn't attached to the desktop now showing.
                InjectPending(pendingInput);
                TypePendingFills(pendingFills);

                // No desktop to capture yet (the first poll gave a zero handle):
                // wait and look again rather than spin.
                if (pipeline is null)
                {
                    captureCts.Token.WaitHandle.WaitOne(CaptureRetryDelayMs);
                    continue;
                }

                ScreenUpdate update;
                try
                {
                    update = pipeline.CaptureNextUpdate();
                }
                catch (Exception ex)
                {
                    // Capturing fails while the secure desktop is up and works
                    // again once it's gone (and the follower will have moved us
                    // to it by then). Retrying keeps the stream alive.
                    Console.WriteLine($"Desk screen capture failed, retrying: {ex.Message}");
                    captureCts.Token.WaitHandle.WaitOne(CaptureRetryDelayMs);
                    continue;
                }

                var now = Environment.TickCount64;
                cadence.Captured(update.Regions.Count > 0, now);
                var groups = ScreenFrame.Split(update.Regions);
                var firstId = groups.Count > 0 ? credit.Send(now, groups.Count) : 0;
                var messages = groups
                    .Select((regions, i) => new ScreenFrame(firstId + i, update.Width, update.Height, regions).Encode())
                    .ToList();
                var workMs = (int)sw.ElapsedMilliseconds;
                foreach (var payload in messages)
                {
                    channel.Send(new Midview.Protocol.Message(MessageType.ScreenFrame, payload));
                }

                var rest = cadence.RestMs(workMs, (int)sw.ElapsedMilliseconds, Environment.TickCount64);
                if (rest > 0)
                {
                    captureCts.Token.WaitHandle.WaitOne(rest);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Only a failed send gets here, so the connection is dead. Closing
            // the socket is what unblocks the receive loop's blocked Receive(),
            // which isn't watching any cancellation token.
            Console.WriteLine($"Desk capture loop stopped: {ex.Message}");
            tcpClient.Close();
        }
        finally
        {
            // Free the live pipeline's bitmaps when the worker ends, the same
            // reason the switch above disposes the superseded one.
            pipeline?.Dispose();
        }
    }

    // Types each queued admin password and Enter, on the worker thread. The
    // decoded bytes are cleared once typed (the transient string made for the
    // injector can't be zeroed; that residual is accepted in the spec).
    private void TypePendingFills(ConcurrentQueue<byte[]> pendingFills)
    {
        while (pendingFills.TryDequeue(out var passwordUtf8))
        {
            try
            {
                _inputInjector.TypeText(System.Text.Encoding.UTF8.GetString(passwordUtf8), pressEnter: true);
            }
            catch (Exception ex)
            {
                // Never log the password; one failed fill must not kill the
                // worker or the screen stream.
                Console.WriteLine($"Typing a Desk password fill failed: {ex.Message}");
            }
            finally
            {
                Array.Clear(passwordUtf8);
            }
        }
    }

    private void InjectPending(ConcurrentQueue<InputEvent> pendingInput)
    {
        while (pendingInput.TryDequeue(out var inputEvent))
        {
            try
            {
                _inputInjector.Inject(inputEvent);
            }
            catch (Exception ex)
            {
                // One event that won't inject must not kill the worker or the
                // screen stream; log it and carry on with the next.
                Console.WriteLine($"Injecting a Desk input event failed: {ex.Message}");
            }
        }
    }
}
