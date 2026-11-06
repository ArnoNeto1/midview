using System.Drawing;
using System.Net;
using System.Net.Sockets;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class CountingScreenCapture : IScreenCapture
{
    private int _colorSeed;
    public int CallCount { get; private set; }

    public Bitmap CaptureFrame()
    {
        CallCount++;
        _colorSeed = (_colorSeed + 1) % 255;
        var bitmap = new Bitmap(64, 64);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(_colorSeed, _colorSeed, _colorSeed)); // always different from the previous frame
        return bitmap;
    }
}

public class FakeInputInjector : IInputInjector
{
    public int InjectCount { get; private set; }
    public void Inject(InputEvent inputEvent) => InjectCount++;
    public void TypeText(string text, bool pressEnter)
    {
    }
}

// Throws like GdiScreenCapture does while the secure desktop (Win+L, a UAC
// prompt, Ctrl+Alt+Del) is showing, then recovers.
public class FailingThenWorkingScreenCapture : IScreenCapture
{
    private int _failuresLeft;

    public FailingThenWorkingScreenCapture(int failures)
    {
        _failuresLeft = failures;
    }

    public Bitmap CaptureFrame()
    {
        if (_failuresLeft-- > 0)
        {
            throw new System.ComponentModel.Win32Exception("The handle is invalid");
        }
        return new Bitmap(64, 64);
    }
}

public class FixedSizeScreenCapture : IScreenCapture
{
    private readonly int _width;
    private readonly int _height;

    public FixedSizeScreenCapture(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public Bitmap CaptureFrame() => new(_width, _height);
}

public class RecordingCountdownNotice : ICountdownNotice
{
    private readonly List<(string Message, string Done, TimeSpan Duration, TimeSpan Linger)> _shown = new();

    public IReadOnlyList<(string Message, string Done, TimeSpan Duration, TimeSpan Linger)> Shown
    {
        get
        {
            lock (_shown)
            {
                return _shown.ToList();
            }
        }
    }

    public void Show(string message, string doneMessage, TimeSpan duration, TimeSpan linger)
    {
        lock (_shown)
        {
            _shown.Add((message, doneMessage, duration, linger));
        }
    }
}

public class RecordingMessageDisplay : IMessageDisplay
{
    private readonly List<string> _shown = new();
    private readonly List<string> _events = new();

    public IReadOnlyList<string> Shown
    {
        get
        {
            lock (_shown)
            {
                return _shown.ToList();
            }
        }
    }

    // Every Show and CloseAll, in order: "show: <text>" or "close all".
    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_shown)
            {
                return _events.ToList();
            }
        }
    }

    public void Show(string text)
    {
        lock (_shown)
        {
            _shown.Add(text);
            _events.Add("show: " + text);
        }
    }

    public void CloseAll()
    {
        lock (_shown)
        {
            _events.Add("close all");
        }
    }
}

// Records what the Session asked the chat window to do, and lets a test
// type a line "as the student" through the callback it was given.
public class FakeChatWindows : IChatWindows
{
    private readonly List<FakeChatWindow> _opened = new();

    public IReadOnlyList<FakeChatWindow> Opened
    {
        get
        {
            lock (_opened)
            {
                return _opened.ToList();
            }
        }
    }

    public IChatWindow Open(Func<string, bool> sendToTeacher)
    {
        var window = new FakeChatWindow(sendToTeacher);
        lock (_opened)
        {
            _opened.Add(window);
        }
        return window;
    }
}

public class FakeChatWindow : IChatWindow
{
    private readonly List<string> _teacherLines = new();

    public FakeChatWindow(Func<string, bool> sendToTeacher)
    {
        StudentTypes = sendToTeacher;
    }

    public Func<string, bool> StudentTypes { get; }

    public bool Ended { get; private set; }

    public IReadOnlyList<string> TeacherLines
    {
        get
        {
            lock (_teacherLines)
            {
                return _teacherLines.ToList();
            }
        }
    }

    public void ShowTeacherMessage(string text)
    {
        lock (_teacherLines)
        {
            _teacherLines.Add(text);
        }
    }

    public void ShowConversationEnded() => Ended = true;
}

// Records each "close programs" request and answers with fixed counts.
public class FakeOpenProgramsLister : IOpenProgramsLister
{
    private readonly IReadOnlyList<OpenProgram> _programs;

    public FakeOpenProgramsLister(params OpenProgram[] programs)
    {
        _programs = programs;
    }

    public IReadOnlyList<OpenProgram> List() => _programs;
}

public class FakeAppCloser : IAppCloser
{
    private readonly (int Asked, int Forced) _result;
    private readonly List<bool> _calls = new();

    public FakeAppCloser(int asked, int forced)
    {
        _result = (asked, forced);
    }

    public IReadOnlyList<bool> ForceFlags
    {
        get
        {
            lock (_calls)
            {
                return _calls.ToList();
            }
        }
    }

    public (int Asked, int Forced) CloseAll(bool force)
    {
        lock (_calls)
        {
            _calls.Add(force);
        }
        return _result;
    }
}

public class RecordingScreenLock : IScreenLockDisplay
{
    private int _shows;
    private bool _showing;

    public int Shows => Volatile.Read(ref _shows);
    public bool Showing => Volatile.Read(ref _showing);

    public void Show()
    {
        Interlocked.Increment(ref _shows);
        Volatile.Write(ref _showing, true);
    }

    public void Hide() => Volatile.Write(ref _showing, false);
}

public class RecordingFileOpener : IFileOpener
{
    private readonly List<string> _opened = new();

    public IReadOnlyList<string> Opened
    {
        get
        {
            lock (_opened)
            {
                return _opened.ToList();
            }
        }
    }

    public void Open(string path)
    {
        lock (_opened)
        {
            _opened.Add(path);
        }
    }
}

public class RecordingProgramStarter : IProgramStarter
{
    private readonly List<(string Program, string Arguments)> _started = new();

    // When set, Start throws it instead, as Windows does for a program it can't find.
    public Exception? Failure { get; init; }

    // What Start answers: false is "still waiting after a while".
    public bool Finishes { get; init; } = true;

    public IReadOnlyList<(string Program, string Arguments)> Started
    {
        get
        {
            lock (_started)
            {
                return _started.ToList();
            }
        }
    }

    public bool Start(string program, string arguments)
    {
        if (Failure is not null)
        {
            throw Failure;
        }
        lock (_started)
        {
            _started.Add((program, arguments));
        }
        return Finishes;
    }
}

public class RecordingWebsiteOpener : IWebsiteOpener
{
    private readonly List<string> _opened = new();

    // When set, Open throws it instead, as Windows does with no browser set up.
    public Exception? Failure { get; init; }

    public IReadOnlyList<string> Opened
    {
        get
        {
            lock (_opened)
            {
                return _opened.ToList();
            }
        }
    }

    public void Open(Uri address)
    {
        if (Failure is not null)
        {
            throw Failure;
        }
        lock (_opened)
        {
            _opened.Add(address.AbsoluteUri);
        }
    }
}

// Moves what it's given into a folder of its own, or refuses the names it's told to.
public class FakeRecycleBin : IRecycleBin
{
    private readonly HashSet<string> _refused;

    public FakeRecycleBin(string binDirectory, params string[] refusedNames)
    {
        BinDirectory = binDirectory;
        _refused = new HashSet<string>(refusedNames, StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(binDirectory);
    }

    public string BinDirectory { get; }

    public void Recycle(string path)
    {
        if (_refused.Contains(Path.GetFileName(path)))
        {
            throw new IOException("in use", unchecked((int)0x80070020));
        }
        var target = Path.Combine(BinDirectory, Path.GetFileName(path));
        if (Directory.Exists(path))
        {
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target);
        }
    }
}

public partial class SessionServerTests
{
    [Fact]
    public void CaptureCadence_IsDecoupledFromMessageRate()
    {
        var capture = new CountingScreenCapture();
        var injector = new FakeInputInjector();
        var serverCert = SelfSignedCertificate.Create("session-under-test");
        var clientCert = SelfSignedCertificate.Create("master-under-test");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var server = new SessionServer(
            listener, serverCert, new TrustStore(new[] { clientCert.Thumbprint }),
            capture, injector,
            new InputLockManager(new FakeInputDeviceController(), new FakeClock(), TimeSpan.FromSeconds(5)));

        using var stopServer = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.Run(stopServer.Token));

        using var tcpClient = new TcpClient();
        tcpClient.Connect(IPAddress.Loopback, port);
        using var clientChannel = SecureChannel.AuthenticateAsClient(
            tcpClient, "session-under-test", clientCert, new TrustStore(new[] { serverCert.Thumbprint }));

        clientChannel.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));

        // Flood far more input events than any capture pipeline should ever
        // process 1:1 -- if capture were still tied to message arrival, this
        // alone would drive thousands of captures in well under a second.
        var inputPayload = new byte[11];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(1))
        {
            clientChannel.Send(new Message(MessageType.InputEvent, inputPayload));
        }

        clientChannel.Send(new Message(MessageType.StopControl, Array.Empty<byte>()));
        tcpClient.Client.Shutdown(SocketShutdown.Send);
        Thread.Sleep(300); // let the capture thread's in-flight interval finish
        stopServer.Cancel();
        listener.Stop(); // unblocks the now-idle AcceptTcpClient() with an expected shutdown exception
        try
        {
            serverTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Expected: stopping the listener while Run()'s loop is blocked
            // in AcceptTcpClient() makes it throw -- that's this test's own
            // shutdown mechanism, not a defect under test.
        }

        // ~1s at a 66ms capture interval is ~15 captures; assert well under
        // the thousands a message-coupled loop would have produced instead.
        Assert.InRange(capture.CallCount, 1, 60);
    }

    [Fact]
    public async Task CaptureFailures_AreRetried_AndFramesStillArrive()
    {
        using var harness = new ServerHarness(new FailingThenWorkingScreenCapture(failures: 3));
        using var master = harness.ConnectMaster();
        master.StartControl();

        var firstMessage = await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(MessageType.FrameTile, firstMessage.Type);
    }

    // What a Master from before ScreenStream sends when it opens just to
    // watch: a heartbeat, not StartControl (the current one sends ScreenStream
    // first, see the ScreenStream_ tests). The screen must arrive, and the
    // student keeps their own keyboard and mouse.
    [Fact]
    public async Task WatchingOnly_SendsTheScreen_WithoutLockingOrInjecting()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
        master.Channel.Send(new Message(MessageType.InputEvent, MouseMoveTo(10, 10)));

        var firstMessage = await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));
        master.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
        await Task.Delay(300); // long enough for the Session to have read both

        Assert.Equal(MessageType.FrameTile, firstMessage.Type);
        Assert.Equal(0, harness.Devices.DisableCalls);
        Assert.Equal(0, harness.Injector.InjectCount);
    }

    [Fact]
    public async Task FromWatchingToControlAndBack_LocksThenReleasesTheStudent()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
        await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10)); // watching

        master.StartControl();
        master.Channel.Send(new Message(MessageType.InputEvent, MouseMoveTo(10, 10)));
        await WaitUntil(() => harness.Devices.DisableCalls == 1 && harness.Injector.InjectCount == 1);

        master.Channel.Send(new Message(MessageType.StopControl, Array.Empty<byte>()));
        master.Channel.Send(new Message(MessageType.InputEvent, MouseMoveTo(20, 20)));
        await WaitUntil(() => harness.Devices.EnableCalls == 1);
        await Task.Delay(300);

        Assert.Equal(1, harness.Injector.InjectCount); // watching again: input is dropped
    }

    // A malformed input-event payload must not crash the control loop:
    // InputEventCodec.Decode has no length guard, so the Session drops the
    // connection (releasing the input lock the control hold took) instead of
    // throwing, the same guard DeskServer uses.
    [Fact]
    public async Task AMalformedInputEvent_DropsTheConnection_AndReleasesTheLock()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
        await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10)); // watching

        master.StartControl();
        // Too short for InputEventCodec.Decode, which has no length guard.
        master.Channel.Send(new Message(MessageType.InputEvent, Array.Empty<byte>()));

        // The connection is dropped and the lock the control hold took is
        // released, rather than the receive loop crashing.
        await WaitUntil(() => harness.Devices.EnableCalls == 1);
        Assert.Equal(0, harness.Injector.InjectCount);
    }

    // What ViewControlWindow sends first now: ScreenStream, then a heartbeat
    // to only watch. The screen comes as ScreenFrame updates, with its size.
    [Fact]
    public async Task ScreenStream_SendsScreenFrames_WithTheScreensSize()
    {
        using var harness = new ServerHarness(new FixedSizeScreenCapture(200, 100));
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        master.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));

        var message = await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(MessageType.ScreenFrame, message.Type);
        var frame = ScreenFrame.Decode(message.Payload);
        Assert.NotNull(frame);
        Assert.Equal((1, 200, 100), (frame.FrameId, frame.ScreenWidth, frame.ScreenHeight));
        var region = Assert.Single(frame.Regions);
        Assert.Equal((0, 0, 200, 100), (region.X, region.Y, region.Width, region.Height));
        Assert.Equal(0, harness.Devices.DisableCalls);
    }

    // Never more than two updates on their way: the next waits for the
    // viewer to say it has drawn them. The Session would give up waiting
    // only after a minute here, so only the acknowledgement can release it.
    [Fact]
    public async Task ScreenStream_WaitsForTheViewer_BeforeSendingMore()
    {
        using var harness = new ServerHarness(
            new CountingScreenCapture(), frameAckTimeout: TimeSpan.FromMinutes(1)); // different on every capture
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        master.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));

        await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));
        var third = Task.Run(() => master.Channel.Receive());
        await Task.Delay(500);
        Assert.False(third.IsCompleted);

        master.Channel.Send(new Message(MessageType.FrameAck, ScreenFrame.EncodeAck(2)));
        var message = await third.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.ScreenFrame, message.Type);
        Assert.Equal(3, ScreenFrame.Decode(message.Payload)!.FrameId);
    }

    // A viewer that skips acknowledging (an update it couldn't read, say)
    // must not freeze its picture: after the timeout the Session carries on.
    [Fact]
    public async Task ScreenStream_ViewerThatNeverAcknowledges_StillGetsUpdates()
    {
        using var harness = new ServerHarness(
            new CountingScreenCapture(), frameAckTimeout: TimeSpan.FromMilliseconds(200));
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        master.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));

        for (var expected = 1; expected <= 4; expected++)
        {
            var message = await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(MessageType.ScreenFrame, message.Type);
            Assert.Equal(expected, ScreenFrame.Decode(message.Payload)!.FrameId);
        }
    }

    // As with FrameTile: the secure desktop (Win+L, a UAC prompt) makes
    // capturing fail for a while, and the stream must pick up afterwards.
    [Fact]
    public async Task ScreenStream_CaptureFailures_AreRetried_AndFramesStillArrive()
    {
        using var harness = new ServerHarness(new FailingThenWorkingScreenCapture(failures: 3));
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        master.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));

        var message = await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(MessageType.ScreenFrame, message.Type);
        Assert.Equal(1, ScreenFrame.Decode(message.Payload)!.FrameId);
    }

    [Fact]
    public async Task ScreenStream_ThenStartControl_LocksAndInjects()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        master.StartControl();
        master.Channel.Send(new Message(MessageType.InputEvent, MouseMoveTo(10, 10)));

        var message = await Task.Run(() => master.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntil(() => harness.Devices.DisableCalls == 1 && harness.Injector.InjectCount == 1);

        Assert.Equal(MessageType.ScreenFrame, message.Type);
    }

    [Fact]
    public async Task LockScreen_CoversTheScreenAndLocksInput_UntilTheConnectionCloses()
    {
        var screenLock = new RecordingScreenLock();
        using var harness = new ServerHarness(new CountingScreenCapture(), screenLock: screenLock);
        var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.LockScreen()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal((true, "tela bloqueada"), result);
        Assert.True(screenLock.Showing);
        Assert.Equal(1, harness.Devices.DisableCalls);

        teacher.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
        await Task.Delay(300);
        Assert.True(screenLock.Showing);

        teacher.Dispose(); // "Desbloquear", or the teacher's program closing
        await WaitUntil(() => !screenLock.Showing && harness.Devices.EnableCalls == 1);
    }

    [Fact]
    public async Task LockScreen_StaysOn_WhileControlStartsAndStops()
    {
        var screenLock = new RecordingScreenLock();
        using var harness = new ServerHarness(new CountingScreenCapture(), screenLock: screenLock);
        using var locking = harness.ConnectMaster();
        await Task.Run(() => locking.LockScreen()).WaitAsync(TimeSpan.FromSeconds(5));

        using var controlling = harness.ConnectMaster();
        controlling.StartControl();
        await Task.Run(() => controlling.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10)); // it holds the control slot now
        controlling.Channel.Send(new Message(MessageType.StopControl, Array.Empty<byte>()));
        await Task.Delay(300);

        Assert.True(screenLock.Showing);
        Assert.Equal(0, harness.Devices.EnableCalls);
    }

    [Fact]
    public async Task TwoLocks_KeepTheScreenLocked_UntilBothEnd()
    {
        var screenLock = new RecordingScreenLock();
        using var harness = new ServerHarness(new CountingScreenCapture(), screenLock: screenLock);
        var first = harness.ConnectMaster();
        using var second = harness.ConnectMaster();
        await Task.Run(() => first.LockScreen()).WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Run(() => second.LockScreen()).WaitAsync(TimeSpan.FromSeconds(5));

        first.Dispose();
        await Task.Delay(500);

        Assert.True(screenLock.Showing);
        Assert.Equal(1, screenLock.Shows);
        Assert.Equal(0, harness.Devices.EnableCalls);
    }

    [Fact]
    public async Task LockScreen_WithoutALockScreen_IsRefusedWithoutLockingInput()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.LockScreen()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Equal(0, harness.Devices.DisableCalls);
    }

    // How a Master that sees and drives the screen through another process
    // locks the student's keyboard and mouse: this connection, which streams
    // nothing (see HoldInputLock).
    [Fact]
    public async Task HoldInputLock_locks_the_student_input()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var master = harness.ConnectMaster();

        master.Channel.Send(new Message(MessageType.HoldInputLock, Array.Empty<byte>()));

        await WaitUntil(() => harness.InputLock.IsLocked);
        Assert.Equal(1, harness.Devices.DisableCalls);
    }

    [Fact]
    public async Task HoldInputLock_sends_no_frames()
    {
        var capture = new CountingScreenCapture();
        using var harness = new ServerHarness(capture);
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.HoldInputLock, Array.Empty<byte>()));
        await WaitUntil(() => harness.InputLock.IsLocked);

        // A connection that streamed the screen would have sent its first
        // frame within a capture interval or two.
        var next = Task.Run(() => master.Channel.Receive());
        await Task.Delay(500);

        Assert.False(next.IsCompleted);
        Assert.Equal(0, capture.CallCount);

        master.Dispose(); // the read above ends with the connection
        await Assert.ThrowsAnyAsync<Exception>(() => next).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // The slot is for the one Master that streams the screen: a hold that
    // took it would keep every other Master out for as long as it lasts.
    [Fact]
    public async Task HoldInputLock_does_not_take_the_controlling_slot()
    {
        using var harness = new ServerHarness(new FixedSizeScreenCapture(200, 100));
        using var holding = harness.ConnectMaster();
        holding.Channel.Send(new Message(MessageType.HoldInputLock, Array.Empty<byte>()));
        await WaitUntil(() => harness.InputLock.IsLocked);

        using var watching = harness.ConnectMaster();
        watching.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        watching.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));

        // A Session that gave the hold the slot would make this one wait 15 s for it.
        var message = await Task.Run(() => watching.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.ScreenFrame, message.Type);
    }

    // The Master's heartbeats keep the hold from timing out as they do a
    // control connection's: with one 4 s ago a lock 8 s old stays, and with
    // none for 6 s it ends.
    [Fact]
    public async Task HoldInputLock_heartbeats_keep_the_lock_from_timing_out()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.HoldInputLock, Array.Empty<byte>()));
        await WaitUntil(() => harness.InputLock.IsLocked);

        harness.Clock.Advance(TimeSpan.FromSeconds(4));
        var readsBefore = harness.Clock.Reads;
        master.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
        await WaitUntil(() => harness.Clock.Reads > readsBefore); // the Session has taken the heartbeat

        harness.Clock.Advance(TimeSpan.FromSeconds(4));
        harness.InputLock.CheckHeartbeatTimeout();
        Assert.True(harness.InputLock.IsLocked);

        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        harness.InputLock.CheckHeartbeatTimeout();
        Assert.False(harness.InputLock.IsLocked);
    }

    // Closing the connection is the Master's window closing or its program
    // ending. The clock never moves here and no heartbeat comes, so only the
    // connection's end can have released it.
    [Fact]
    public async Task Dropping_the_hold_unlocks()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        var master = harness.ConnectMaster();
        master.Channel.Send(new Message(MessageType.HoldInputLock, Array.Empty<byte>()));
        await WaitUntil(() => harness.InputLock.IsLocked);

        master.Dispose();

        await WaitUntil(() => !harness.InputLock.IsLocked);
        Assert.Equal(1, harness.Devices.EnableCalls);
    }

    // A window that only watches ends all the time, and it never took the
    // lock: the student must not get the keyboard back because of it while a
    // hold (the teacher controlling through another process) still has it.
    [Fact]
    public async Task A_hold_stays_locked_when_a_watching_connection_ends()
    {
        using var harness = new ServerHarness(new FixedSizeScreenCapture(200, 100));
        using var holding = harness.ConnectMaster();
        holding.Channel.Send(new Message(MessageType.HoldInputLock, Array.Empty<byte>()));
        await WaitUntil(() => harness.InputLock.IsLocked);

        await WatchAndLeave(harness, stopControlOnTheWayOut: false);

        Assert.True(harness.InputLock.IsLocked);
        Assert.Equal(0, harness.Devices.EnableCalls);
    }

    // The Master's window sends StopControl as it closes, controlling or not.
    [Fact]
    public async Task A_hold_stays_locked_when_a_Master_that_never_controlled_stops_controlling()
    {
        using var harness = new ServerHarness(new FixedSizeScreenCapture(200, 100));
        using var holding = harness.ConnectMaster();
        holding.Channel.Send(new Message(MessageType.HoldInputLock, Array.Empty<byte>()));
        await WaitUntil(() => harness.InputLock.IsLocked);

        await WatchAndLeave(harness, stopControlOnTheWayOut: true);

        Assert.True(harness.InputLock.IsLocked);
        Assert.Equal(0, harness.Devices.EnableCalls);
    }

    // A Master that reconnects while the Session still has the connection it
    // replaces: the old one ends (on its own timeout, or when its closed
    // socket finally gets here) with the new one in use.
    [Fact]
    public async Task Two_overlapping_holds_keep_the_lock_until_both_end()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var first = harness.ConnectMaster();
        first.Channel.Send(new Message(MessageType.HoldInputLock, Array.Empty<byte>()));
        await WaitUntil(() => harness.InputLock.IsLocked);

        using var second = harness.ConnectMaster();
        var readsBefore = harness.Clock.Reads;
        second.Channel.Send(new Message(MessageType.HoldInputLock, Array.Empty<byte>()));
        second.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
        await WaitUntil(() => harness.Clock.Reads > readsBefore); // the Session has taken the second hold

        first.Dispose();
        await Task.Delay(500); // long enough for the Session to have seen it end

        Assert.True(harness.InputLock.IsLocked);
        Assert.Equal(0, harness.Devices.EnableCalls);

        second.Dispose();

        await WaitUntil(() => !harness.InputLock.IsLocked);
        Assert.Equal(1, harness.Devices.EnableCalls);
    }

    // One lock per connection, however often it asks: it ends with the one
    // StopControl, not one per StartControl.
    [Fact]
    public async Task A_repeated_StartControl_still_ends_with_one_StopControl()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var master = harness.ConnectMaster();
        master.StartControl();
        master.StartControl();
        await WaitUntil(() => harness.InputLock.IsLocked);

        master.Channel.Send(new Message(MessageType.StopControl, Array.Empty<byte>()));

        await WaitUntil(() => !harness.InputLock.IsLocked);
        Assert.Equal(1, harness.Devices.EnableCalls);
    }

    // The 11-byte InputEvent ViewControlWindow sends: kind, x, y, key.
    private static byte[] MouseMoveTo(int x, int y)
    {
        var payload = new byte[11];
        BitConverter.GetBytes(x).CopyTo(payload, 1);
        BitConverter.GetBytes(y).CopyTo(payload, 5);
        return payload;
    }

    [Fact]
    public async Task ConnectionThatNeverHandshakes_DoesNotBlockAMaster()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var stalledClient = new TcpClient();
        stalledClient.Connect(IPAddress.Loopback, harness.Port); // then sends nothing at all

        // Well under the server's own 10 s handshake timeout.
        var firstMessage = await Task.Run(() =>
        {
            using var master = harness.ConnectMaster();
            master.StartControl();
            return master.Channel.Receive();
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.FrameTile, firstMessage.Type);
    }

    [Fact]
    public async Task RequestThumbnail_NoControlSession_RepliesWithTheScreenScaledDown()
    {
        using var harness = new ServerHarness(new FixedSizeScreenCapture(1920, 1080));
        using var preview = harness.ConnectMaster();

        var reply = await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.ThumbnailFrame, reply.Type);
        using var thumbnail = TileCodec.DecodeTile(reply.Payload);
        Assert.Equal(ThumbnailRenderer.MaxWidth, thumbnail.Width);
        Assert.Equal(ThumbnailRenderer.MaxHeight, thumbnail.Height);
    }

    [Fact]
    public async Task RequestThumbnail_WithASize_RepliesAtThatSize()
    {
        using var harness = new ServerHarness(new FixedSizeScreenCapture(1920, 1080));
        using var preview = harness.ConnectMaster();

        var small = await Task.Run(() => preview.RequestThumbnail(ThumbnailRequest.Encode(160, 90))).WaitAsync(TimeSpan.FromSeconds(5));
        // The same connection, after the teacher made the tiles bigger.
        var large = await Task.Run(() => preview.RequestThumbnail(ThumbnailRequest.Encode(960, 540))).WaitAsync(TimeSpan.FromSeconds(5));

        using var smallThumbnail = TileCodec.DecodeTile(small.Payload);
        using var largeThumbnail = TileCodec.DecodeTile(large.Payload);
        Assert.Equal((160, 90), (smallThumbnail.Width, smallThumbnail.Height));
        Assert.Equal((960, 540), (largeThumbnail.Width, largeThumbnail.Height));
    }

    [Fact]
    public async Task NoTeacherYet_RefusesConnections_ThenAcceptsTheTeacherOncePaired()
    {
        var trustedMastersFile = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid():N}.json");
        try
        {
            using var harness = new ServerHarness(new FixedSizeScreenCapture(1920, 1080), trustedMastersFile: trustedMastersFile);

            await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() =>
            {
                using var stranger = harness.ConnectMaster();
                stranger.RequestThumbnail();
            }).WaitAsync(TimeSpan.FromSeconds(30)));

            // What the Agent does when this teacher pairs.
            TrustedMastersFile.Add(trustedMastersFile, harness.MasterThumbprint);

            using var preview = harness.ConnectMaster();
            var reply = await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(MessageType.ThumbnailFrame, reply.Type);
        }
        finally
        {
            File.Delete(trustedMastersFile);
        }
    }

    [Fact]
    public async Task RequestThumbnail_WhileAnotherMasterIsControlling_StillReplies()
    {
        using var harness = new ServerHarness(new FixedSizeScreenCapture(640, 360));
        using var controller = harness.ConnectMaster();
        controller.StartControl();
        await Task.Run(() => controller.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5)); // it holds the control slot now

        using var preview = harness.ConnectMaster();

        // Well under the 15 s a second *controlling* Master would wait for
        // the slot, so this only passes if previews don't need it.
        var reply = await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.ThumbnailFrame, reply.Type);
        Assert.NotEmpty(reply.Payload);
    }

    [Fact]
    public async Task ControlConnection_WhilePreviewIsOpen_IsServed()
    {
        using var harness = new ServerHarness(new FixedSizeScreenCapture(640, 360));
        using var preview = harness.ConnectMaster();
        await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(5));

        using var controller = harness.ConnectMaster();
        controller.StartControl();

        var firstMessage = await Task.Run(() => controller.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.FrameTile, firstMessage.Type);
    }

    [Fact]
    public async Task RequestThumbnail_CaptureFails_RepliesEmptyAndKeepsTheConnectionOpen()
    {
        using var harness = new ServerHarness(new FailingThenWorkingScreenCapture(failures: int.MaxValue));
        using var preview = harness.ConnectMaster();

        var first = await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(5));
        var second = await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.ThumbnailFrame, first.Type);
        Assert.Empty(first.Payload);
        Assert.Equal(MessageType.ThumbnailFrame, second.Type);
        Assert.Empty(second.Payload);
    }

    [Fact]
    public async Task ShowMessage_ShowsTheTextAndAcknowledges()
    {
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), display);
        using var sender = harness.ConnectMaster();

        var ack = await Task.Run(() => sender.ShowMessage("Atenção: prova em 5 minutos")).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.CommandAck, ack.Type);
        Assert.Equal(new byte[] { 1 }, ack.Payload);
        Assert.Equal(new[] { "Atenção: prova em 5 minutos" }, display.Shown);
    }

    [Fact]
    public async Task ShowMessage_NothingToShow_RefusesWithoutShowingAnything()
    {
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), display);
        using var sender = harness.ConnectMaster();

        var ack = await Task.Run(() => sender.ShowMessage("   ")).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new byte[] { 0 }, ack.Payload);
        Assert.Empty(display.Shown);
    }

    [Fact]
    public async Task ShowMessage_WhileAnotherMasterIsControlling_IsStillShown()
    {
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), display);
        using var controller = harness.ConnectMaster();
        controller.StartControl();
        await Task.Run(() => controller.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5)); // it holds the control slot now

        using var sender = harness.ConnectMaster();

        // Well under the 15 s a second controlling Master would wait for the slot.
        var ack = await Task.Run(() => sender.ShowMessage("Olá")).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new byte[] { 1 }, ack.Payload);
    }

    [Fact]
    public async Task Chat_OpensAWindowAndShowsTheTeachersLines_IgnoringHeartbeats()
    {
        var chats = new FakeChatWindows();
        using var harness = new ServerHarness(new CountingScreenCapture(), chatWindows: chats);
        using var teacher = harness.ConnectMaster();

        teacher.Channel.Send(new Message(MessageType.ChatOpen, Array.Empty<byte>()));
        teacher.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
        teacher.Channel.Send(new Message(MessageType.ChatText, TextMessage.Encode("Tudo certo aí?")));

        await WaitUntil(() => chats.Opened.Count == 1 && chats.Opened[0].TeacherLines.Count == 1);
        Assert.Equal(new[] { "Tudo certo aí?" }, chats.Opened[0].TeacherLines);
    }

    [Fact]
    public async Task Chat_StudentsLineReachesTheTeacher()
    {
        var chats = new FakeChatWindows();
        using var harness = new ServerHarness(new CountingScreenCapture(), chatWindows: chats);
        using var teacher = harness.ConnectMaster();
        teacher.Channel.Send(new Message(MessageType.ChatOpen, Array.Empty<byte>()));
        await WaitUntil(() => chats.Opened.Count == 1);

        Assert.True(chats.Opened[0].StudentTypes("Não consigo abrir o arquivo"));

        var line = await Task.Run(() => teacher.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(MessageType.ChatText, line.Type);
        Assert.Equal("Não consigo abrir o arquivo", TextMessage.Decode(line.Payload));
    }

    [Fact]
    public async Task Chat_TeacherLeaves_EndsTheConversationOnTheStudentsSide()
    {
        var chats = new FakeChatWindows();
        using var harness = new ServerHarness(new CountingScreenCapture(), chatWindows: chats);
        var teacher = harness.ConnectMaster();
        teacher.Channel.Send(new Message(MessageType.ChatOpen, Array.Empty<byte>()));
        await WaitUntil(() => chats.Opened.Count == 1);

        teacher.Dispose();

        await WaitUntil(() => chats.Opened[0].Ended);
        Assert.False(chats.Opened[0].StudentTypes("alguém aí?"));
    }

    [Fact]
    public async Task Chat_WhileAnotherMasterIsControlling_StillOpens()
    {
        var chats = new FakeChatWindows();
        using var harness = new ServerHarness(new CountingScreenCapture(), chatWindows: chats);
        using var controller = harness.ConnectMaster();
        controller.StartControl();
        await Task.Run(() => controller.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5)); // it holds the control slot now

        using var teacher = harness.ConnectMaster();
        teacher.Channel.Send(new Message(MessageType.ChatOpen, Array.Empty<byte>()));

        await WaitUntil(() => chats.Opened.Count == 1);
    }

    [Fact]
    public async Task SendFile_IsSavedInTheInbox_AndTheStudentIsNotified()
    {
        using var inbox = new TempInbox();
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), display, fileInbox: inbox.Inbox);
        using var teacher = harness.ConnectMaster();
        var content = Enumerable.Range(0, FileTransfer.ChunkSize + 1000).Select(i => (byte)i).ToArray();

        var (accepted, result) = await Task.Run(() => teacher.SendFile("Exercício 1.pdf", content)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(accepted.Saved);
        Assert.Equal((true, "Exercício 1.pdf"), result);
        Assert.Equal(content, File.ReadAllBytes(Path.Combine(inbox.Directory, "Exercício 1.pdf")));
        Assert.Single(display.Shown);
        Assert.Contains("Exercício 1.pdf", display.Shown[0]);
    }

    [Fact]
    public async Task SendFile_WithOverwrite_ReplacesTheOldFile()
    {
        using var inbox = new TempInbox();
        WriteFile(inbox.Directory, "notas.pdf", new byte[] { 1 });
        using var harness = new ServerHarness(new CountingScreenCapture(), fileInbox: inbox.Inbox);
        using var teacher = harness.ConnectMaster();

        var (_, result) = await Task.Run(() => teacher.SendFile("notas.pdf", new byte[] { 2 }, new SendFileOptions(true, AfterSend.Nothing)))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((true, "notas.pdf (substituiu o anterior)"), result);
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(inbox.Directory, "notas.pdf")));
        Assert.Single(Directory.GetFiles(inbox.Directory));
    }

    // An old copy open in a program can't be replaced: the new one is still
    // saved, under another name, and the teacher is told why.
    [Fact]
    public async Task SendFile_WithOverwrite_ButTheOldFileOpen_SavesItBesideIt()
    {
        using var inbox = new TempInbox();
        WriteFile(inbox.Directory, "notas.pdf", new byte[] { 1 });
        using var harness = new ServerHarness(new CountingScreenCapture(), fileInbox: inbox.Inbox);
        using var teacher = harness.ConnectMaster();
        using (new FileStream(Path.Combine(inbox.Directory, "notas.pdf"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var (_, result) = await Task.Run(() => teacher.SendFile("notas.pdf", new byte[] { 2 }, new SendFileOptions(true, AfterSend.Nothing)))
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal((true, "notas (2).pdf (o anterior estava aberto e não pôde ser substituído)"), result);
        }
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(inbox.Directory, "notas (2).pdf")));
    }

    [Fact]
    public async Task SendFile_ThenOpenTheFile_OpensItInsteadOfANotice()
    {
        using var inbox = new TempInbox();
        var display = new RecordingMessageDisplay();
        var opener = new RecordingFileOpener();
        using var harness = new ServerHarness(new CountingScreenCapture(), display, fileInbox: inbox.Inbox, fileOpener: opener);
        using var teacher = harness.ConnectMaster();

        var (_, result) = await Task.Run(() => teacher.SendFile("aula.pdf", new byte[] { 1 }, new SendFileOptions(false, AfterSend.OpenFile)))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((true, "aula.pdf; aberto no aluno"), result);
        Assert.Equal(new[] { Path.Combine(inbox.Directory, "aula.pdf") }, opener.Opened);
        Assert.Empty(display.Shown);
    }

    [Theory]
    [InlineData("script.bat")]
    [InlineData("instalador.exe")]
    public async Task SendFile_ThenOpenAScriptOrProgram_ExecutesIt(string fileName)
    {
        using var inbox = new TempInbox();
        var display = new RecordingMessageDisplay();
        var opener = new RecordingFileOpener();
        using var harness = new ServerHarness(new CountingScreenCapture(), display, fileInbox: inbox.Inbox, fileOpener: opener);
        using var teacher = harness.ConnectMaster();

        var (_, result) = await Task.Run(() => teacher.SendFile(fileName, new byte[] { 1 }, new SendFileOptions(false, AfterSend.OpenFile)))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((true, $"{fileName}; aberto no aluno"), result);
        Assert.Equal(new[] { Path.Combine(inbox.Directory, fileName) }, opener.Opened);
        Assert.Empty(display.Shown);
    }

    [Fact]
    public async Task SendFile_ThenOpenTheFolder_OpensTheInbox()
    {
        using var inbox = new TempInbox();
        var opener = new RecordingFileOpener();
        using var harness = new ServerHarness(new CountingScreenCapture(), fileInbox: inbox.Inbox, fileOpener: opener);
        using var teacher = harness.ConnectMaster();

        var (_, result) = await Task.Run(() => teacher.SendFile("aula.pdf", new byte[] { 1 }, new SendFileOptions(false, AfterSend.OpenFolder)))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((true, "aula.pdf; pasta aberta no aluno"), result);
        Assert.Equal(new[] { inbox.Directory }, opener.Opened);
    }

    [Fact]
    public async Task SendFile_MoreBytesThanDeclared_IsDiscarded()
    {
        using var inbox = new TempInbox();
        using var harness = new ServerHarness(new CountingScreenCapture(), fileInbox: inbox.Inbox);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() =>
        {
            teacher.Channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader("a.txt", 3)));
            Assert.True(CommandResult.Decode(teacher.Channel.Receive().Payload).Succeeded);
            teacher.Channel.Send(new Message(MessageType.FileChunk, new byte[] { 1, 2, 3, 4 }));
            return CommandResult.Decode(teacher.Channel.Receive().Payload);
        }).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.Empty(Directory.GetFileSystemEntries(inbox.Directory));
    }

    [Fact]
    public async Task SendFile_NoInbox_IsRefusedBeforeAnythingIsStreamed()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var teacher = harness.ConnectMaster();

        var accepted = await Task.Run(() =>
        {
            teacher.Channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader("a.txt", 3)));
            return CommandResult.Decode(teacher.Channel.Receive().Payload);
        }).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(accepted.Succeeded);
        Assert.NotEmpty(accepted.Detail);
    }

    [Fact]
    public async Task CloseApps_Ask_AsksWithoutForcing_AndSaysHowManyWindows()
    {
        var closer = new FakeAppCloser(asked: 3, forced: 0);
        using var harness = new ServerHarness(new CountingScreenCapture(), appCloser: closer);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.CloseApps(CloseAppsMode.Ask)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        Assert.Contains("3 janela", result.Detail);
        Assert.Equal(new[] { false }, closer.ForceFlags);
    }

    // What a MidviewMaster from before the warning sends: just the mode.
    [Fact]
    public async Task CloseApps_FromAnOlderMaster_ClosesRightAway()
    {
        var closer = new FakeAppCloser(asked: 2, forced: 0);
        using var harness = new ServerHarness(new CountingScreenCapture(), appCloser: closer);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() =>
        {
            teacher.Channel.Send(new Message(MessageType.CloseApps, new[] { (byte)CloseAppsMode.Ask }));
            return CommandResult.Decode(teacher.Channel.Receive().Payload);
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal((true, "pedido de fechamento enviado a 2 janela(s)"), result);
        Assert.Equal(new[] { false }, closer.ForceFlags);
    }

    [Fact]
    public async Task CloseApps_Force_SaysHowManyWereEndedByForce()
    {
        var closer = new FakeAppCloser(asked: 3, forced: 1);
        using var harness = new ServerHarness(new CountingScreenCapture(), appCloser: closer);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.CloseApps(CloseAppsMode.Force)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        Assert.Contains("1 programa(s) encerrado(s) à força", result.Detail);
        Assert.Equal(new[] { true }, closer.ForceFlags);
    }

    [Fact]
    public async Task CloseApps_NothingOpen_SaysSo()
    {
        using var harness = new ServerHarness(new CountingScreenCapture(), appCloser: new FakeAppCloser(asked: 0, forced: 0));
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.CloseApps(CloseAppsMode.Ask)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal((true, "nenhum programa para fechar"), result);
    }

    [Fact]
    public async Task CloseApps_WithAWarning_TellsTheStudentFirst_AndClosesWhenItsUp()
    {
        var closer = new FakeAppCloser(asked: 3, forced: 0);
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), messageDisplay: display, appCloser: closer);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.CloseApps(CloseAppsMode.Ask, delaySeconds: 1)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal((true, "aluno avisado; os programas fecham em 1 segundo"), result);
        Assert.Empty(closer.ForceFlags); // not yet: the warning's time is the student's
        Assert.Equal(new[] { "O professor vai fechar seus programas em 1 segundo. Salve seu trabalho agora." }, display.Shown);

        await WaitUntil(() => closer.ForceFlags.Count == 1);
        Assert.Equal(new[] { false }, closer.ForceFlags);
    }

    // The teacher's messages are this program's own windows, which closing the
    // programs never touches (WindowsAppCloser leaves Midview alone): they
    // close with the programs, before the notice that says so.
    [Fact]
    public async Task CloseApps_Now_AlsoClosesTheTeachersMessages_BeforeSayingSo()
    {
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(
            new CountingScreenCapture(), messageDisplay: display, appCloser: new FakeAppCloser(asked: 1, forced: 0));
        using var teacher = harness.ConnectMaster();

        await Task.Run(() => teacher.CloseApps(CloseAppsMode.Ask)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "close all", "show: O professor está fechando seus programas." }, display.Events);
    }

    [Fact]
    public async Task CloseApps_WithAWarning_ClosesTheTeachersMessages_WhenTheProgramsClose()
    {
        var closer = new FakeAppCloser(asked: 1, forced: 0);
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), messageDisplay: display, appCloser: closer);
        using var teacher = harness.ConnectMaster();

        await Task.Run(() => teacher.CloseApps(CloseAppsMode.Ask, delaySeconds: 1)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("close all", display.Events); // not yet: the warning's time is the student's

        await WaitUntil(() => display.Events.Contains("close all"));
        Assert.Equal(
            new[] { "show: O professor vai fechar seus programas em 1 segundo. Salve seu trabalho agora.", "close all" },
            display.Events);
    }

    [Fact]
    public async Task CloseApps_ForceWithAWarning_SaysUnsavedWorkIsLost_ThenForces()
    {
        var closer = new FakeAppCloser(asked: 0, forced: 2);
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), messageDisplay: display, appCloser: closer);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.CloseApps(CloseAppsMode.Force, delaySeconds: 1)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal((true, "aluno avisado; os programas fecham em 1 segundo"), result);
        Assert.Equal(
            new[] { "O professor vai fechar seus programas em 1 segundo. Salve seu trabalho agora: o que não estiver salvo será perdido." },
            display.Shown);

        await WaitUntil(() => closer.ForceFlags.Count == 1);
        Assert.Equal(new[] { true }, closer.ForceFlags);
    }

    // With the countdown banner, the warning counts down on the student's
    // screen instead of a message saying "em 1 minuto" once.
    [Theory]
    [InlineData(CloseAppsMode.Ask, false)]
    [InlineData(CloseAppsMode.Force, true)]
    public async Task CloseApps_WithAWarning_CountsDownOnTheStudentsScreen(CloseAppsMode mode, bool force)
    {
        var closer = new FakeAppCloser(asked: 3, forced: 0);
        var display = new RecordingMessageDisplay();
        var countdown = new RecordingCountdownNotice();
        using var harness = new ServerHarness(
            new CountingScreenCapture(), messageDisplay: display, appCloser: closer, countdownNotice: countdown);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.CloseApps(mode, delaySeconds: 1)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal((true, "aluno avisado; os programas fecham em 1 segundo"), result);
        Assert.Empty(display.Shown);
        var (message, done) = CountdownText.CloseApps(force);
        Assert.Equal(new[] { (message, done, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8)) }, countdown.Shown);

        await WaitUntil(() => closer.ForceFlags.Count == 1);
        Assert.Equal(new[] { force }, closer.ForceFlags);
    }

    [Theory]
    [InlineData(CloseAppsMode.Ask, "O professor está fechando seus programas.")]
    [InlineData(CloseAppsMode.Force, "O professor encerrou seus programas.")]
    public async Task CloseApps_Now_StillTellsTheStudent(CloseAppsMode mode, string notice)
    {
        var closer = new FakeAppCloser(asked: 1, forced: 0);
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), messageDisplay: display, appCloser: closer);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.CloseApps(mode)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { notice }, display.Shown);
        Assert.Equal(new[] { mode == CloseAppsMode.Force }, closer.ForceFlags);
    }

    [Theory]
    [InlineData(CloseAppsMode.Ask, "O professor está fechando seus programas.")]
    [InlineData(CloseAppsMode.Force, "O professor encerrou seus programas.")]
    public async Task CloseApps_Now_WithTheBanner_ClosesNow_AndShowsA10sNoticeThatClosesItself(CloseAppsMode mode, string notice)
    {
        var closer = new FakeAppCloser(asked: 1, forced: 0);
        var countdown = new RecordingCountdownNotice();
        using var harness = new ServerHarness(new CountingScreenCapture(), appCloser: closer, countdownNotice: countdown);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.CloseApps(mode)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        // Closed on the spot (not after a delay).
        Assert.Equal(new[] { mode == CloseAppsMode.Force }, closer.ForceFlags);
        // A 10s banner that lingers 0 past the count, so it closes itself at the end.
        Assert.Equal(new[] { (notice, notice, TimeSpan.FromSeconds(10), TimeSpan.Zero) }, countdown.Shown);
    }

    [Fact]
    public async Task ListOpenPrograms_AnswersWithWhatTheStudentHasOpen()
    {
        var lister = new FakeOpenProgramsLister(new OpenProgram("chrome.exe", "Google Chrome", 2), new OpenProgram("winword.exe", "Word", 1));
        using var harness = new ServerHarness(new CountingScreenCapture(), openPrograms: lister);
        using var teacher = harness.ConnectMaster();

        var reply = await Task.Run(() =>
        {
            teacher.Channel.Send(new Message(MessageType.ListOpenPrograms, Array.Empty<byte>()));
            return teacher.Channel.Receive();
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.OpenPrograms, reply.Type);
        Assert.True(OpenProgramsReport.TryDecode(reply.Payload, out var programs));
        Assert.Equal(new[] { "chrome.exe", "winword.exe" }, programs.Select(program => program.ExeName));
    }

    [Fact]
    public async Task ListOpenPrograms_IsAnsweredWhileAnotherMasterControlsTheMachine()
    {
        using var harness = new ServerHarness(
            new CountingScreenCapture(), openPrograms: new FakeOpenProgramsLister(new OpenProgram("chrome.exe", "Google Chrome", 1)));
        using var controlling = harness.ConnectMaster();
        controlling.StartControl();
        await Task.Delay(500); // long enough for the control to be under way

        using var asking = harness.ConnectMaster();
        var reply = await Task.Run(() =>
        {
            asking.Channel.Send(new Message(MessageType.ListOpenPrograms, Array.Empty<byte>()));
            return asking.Channel.Receive();
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.OpenPrograms, reply.Type);
    }

    [Fact]
    public async Task ListOpenPrograms_WithoutALister_AnswersAnEmptyList()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var teacher = harness.ConnectMaster();

        var reply = await Task.Run(() =>
        {
            teacher.Channel.Send(new Message(MessageType.ListOpenPrograms, Array.Empty<byte>()));
            return teacher.Channel.Receive();
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageType.OpenPrograms, reply.Type);
        Assert.True(OpenProgramsReport.TryDecode(reply.Payload, out var programs));
        Assert.Empty(programs);
    }

    [Fact]
    public async Task CloseApps_NoCloser_IsRefused()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.CloseApps(CloseAppsMode.Ask)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Detail);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 7 })]
    [InlineData(new byte[] { 1, 1 })]
    public async Task CloseApps_InvalidRequest_IsRefusedWithoutClosingAnything(byte[] payload)
    {
        var closer = new FakeAppCloser(asked: 3, forced: 0);
        using var harness = new ServerHarness(new CountingScreenCapture(), appCloser: closer);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() =>
        {
            teacher.Channel.Send(new Message(MessageType.CloseApps, payload));
            return CommandResult.Decode(teacher.Channel.Receive().Payload);
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.Succeeded);
        Assert.Empty(closer.ForceFlags);
    }

    [Fact]
    public async Task CloseApps_WhileAnotherMasterIsControlling_StillRuns()
    {
        var closer = new FakeAppCloser(asked: 2, forced: 0);
        using var harness = new ServerHarness(new CountingScreenCapture(), appCloser: closer);
        using var controller = harness.ConnectMaster();
        controller.StartControl();
        await Task.Run(() => controller.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5)); // it holds the control slot now

        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.CloseApps(CloseAppsMode.Ask)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CollectFiles_SendsEveryFileInTheFolder_AndTellsTheStudent()
    {
        using var folder = new TempInbox();
        WriteFile(folder.Directory, "trabalho.docx", new byte[] { 1, 2, 3 });
        WriteFile(folder.Directory, "site/index.html", Enumerable.Range(0, FileTransfer.ChunkSize + 10).Select(i => (byte)i).ToArray());
        WriteFile(folder.Directory, "vazio.txt", Array.Empty<byte>());
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), display, deliveryFolder: new DeliveryFolder(folder.Directory));
        using var teacher = harness.ConnectMaster();

        var (files, result) = await Task.Run(teacher.CollectFiles).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((true, string.Empty), result);
        Assert.Equal(new[] { "site/index.html", "trabalho.docx", "vazio.txt" }, files.Keys.Order());
        Assert.Equal(new byte[] { 1, 2, 3 }, files["trabalho.docx"]);
        Assert.Equal(FileTransfer.ChunkSize + 10, files["site/index.html"].Length);
        Assert.Empty(files["vazio.txt"]);
        await WaitUntil(() => display.Shown.Count == 1);
        Assert.Contains("recolheu 3 arquivo(s)", display.Shown[0]);
        Assert.True(File.Exists(Path.Combine(folder.Directory, "trabalho.docx"))); // nothing is removed
    }

    [Fact]
    public async Task CollectFiles_LeavesOutWhatWouldPassTheLimits_AndSaysSo()
    {
        using var folder = new TempInbox();
        WriteFile(folder.Directory, "a.txt", new byte[4]);
        WriteFile(folder.Directory, "b.txt", new byte[20]);
        WriteFile(folder.Directory, "c.txt", new byte[4]);
        WriteFile(folder.Directory, "d.txt", new byte[1]);
        var delivery = new DeliveryFolder(folder.Directory, maxFiles: 2, maxTotalBytes: 10);
        using var harness = new ServerHarness(new CountingScreenCapture(), deliveryFolder: delivery);
        using var teacher = harness.ConnectMaster();

        var (files, result) = await Task.Run(teacher.CollectFiles).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "a.txt", "c.txt" }, files.Keys.Order());
        Assert.True(result.Succeeded);
        Assert.StartsWith("2 não enviado(s): b.txt (passaria do limite", result.Detail);
        Assert.Contains("o que passou de 2 arquivos", result.Detail);
    }

    [Fact]
    public async Task ClearDeliveryFolder_SendsEverythingToTheRecycleBin_AndTellsTheStudent()
    {
        using var folder = new TempInbox();
        WriteFile(folder.Directory, "trabalho.docx", new byte[] { 1 });
        WriteFile(folder.Directory, "site/index.html", new byte[] { 2 });
        var bin = new FakeRecycleBin(folder.Directory + "-lixeira");
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(
            new CountingScreenCapture(), display, deliveryFolder: new DeliveryFolder(folder.Directory), recycleBin: bin);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(teacher.ClearDeliveryFolder).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((true, "2 item(ns) foram para a Lixeira do aluno"), result);
        Assert.Empty(Directory.GetFileSystemEntries(folder.Directory));
        Assert.True(File.Exists(Path.Combine(bin.BinDirectory, "trabalho.docx")));
        await WaitUntil(() => display.Shown.Count == 1);
        Assert.Contains("Lixeira", display.Shown[0]);
        Directory.Delete(bin.BinDirectory, recursive: true);
    }

    [Fact]
    public async Task ClearDeliveryFolder_WhatCantGo_StaysAndIsNamed()
    {
        using var folder = new TempInbox();
        WriteFile(folder.Directory, "trabalho.docx", new byte[] { 1 });
        WriteFile(folder.Directory, "aberto.docx", new byte[] { 2 });
        var bin = new FakeRecycleBin(folder.Directory + "-lixeira", "aberto.docx");
        using var harness = new ServerHarness(
            new CountingScreenCapture(), deliveryFolder: new DeliveryFolder(folder.Directory), recycleBin: bin);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(teacher.ClearDeliveryFolder).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((false, "1 item(ns) foram para a Lixeira do aluno; não saiu: aberto.docx (o arquivo está em uso por outro programa)"), result);
        Assert.True(File.Exists(Path.Combine(folder.Directory, "aberto.docx")));
        Directory.Delete(bin.BinDirectory, recursive: true);
    }

    [Fact]
    public async Task ClearDeliveryFolder_WithoutARecycleBin_IsRefused()
    {
        using var folder = new TempInbox();
        WriteFile(folder.Directory, "trabalho.docx", new byte[] { 1 });
        using var harness = new ServerHarness(new CountingScreenCapture(), deliveryFolder: new DeliveryFolder(folder.Directory));
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(teacher.ClearDeliveryFolder).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(folder.Directory, "trabalho.docx")));
    }

    [Fact]
    public async Task CollectFiles_EmptyFolder_StillAnswers_AndTellsTheStudentWhereItIs()
    {
        using var folder = new TempInbox();
        Directory.CreateDirectory(folder.Directory);
        var display = new RecordingMessageDisplay();
        using var harness = new ServerHarness(new CountingScreenCapture(), display, deliveryFolder: new DeliveryFolder(folder.Directory));
        using var teacher = harness.ConnectMaster();

        var (files, result) = await Task.Run(teacher.CollectFiles).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(files);
        Assert.True(result.Succeeded);
        await WaitUntil(() => display.Shown.Count == 1);
        Assert.Contains(folder.Directory, display.Shown[0]);
    }

    [Fact]
    public async Task CollectFiles_NoDeliveryFolder_IsRefused()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var teacher = harness.ConnectMaster();

        var (files, result) = await Task.Run(teacher.CollectFiles).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(files);
        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Detail);
    }

    [Fact]
    public async Task CollectFiles_WhileAnotherMasterIsControlling_StillRuns()
    {
        using var folder = new TempInbox();
        WriteFile(folder.Directory, "a.txt", new byte[] { 7 });
        using var harness = new ServerHarness(new CountingScreenCapture(), deliveryFolder: new DeliveryFolder(folder.Directory));
        using var controller = harness.ConnectMaster();
        controller.StartControl();
        await Task.Run(() => controller.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5)); // it holds the control slot now

        using var teacher = harness.ConnectMaster();

        var (files, _) = await Task.Run(teacher.CollectFiles).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new byte[] { 7 }, files["a.txt"]);
    }

    [Fact]
    public async Task OpenWebsite_OpensItInTheStudentsBrowser()
    {
        var opener = new RecordingWebsiteOpener();
        using var harness = new ServerHarness(new CountingScreenCapture(), websiteOpener: opener);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.OpenWebsite("www.google.com")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((true, "aberto"), result);
        Assert.Equal(new[] { "https://www.google.com/" }, opener.Opened);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:display")]
    [InlineData("notepad")]
    public async Task OpenWebsite_AnythingButAWebsite_IsRefused_AndNothingOpens(string address)
    {
        var opener = new RecordingWebsiteOpener();
        using var harness = new ServerHarness(new CountingScreenCapture(), websiteOpener: opener);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.OpenWebsite(address)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((false, "não é o endereço de um site"), result);
        Assert.Empty(opener.Opened);
    }

    [Fact]
    public async Task OpenWebsite_WhenTheBrowserDoesntOpen_SaysSo()
    {
        var opener = new RecordingWebsiteOpener { Failure = new InvalidOperationException("nenhum navegador") };
        using var harness = new ServerHarness(new CountingScreenCapture(), websiteOpener: opener);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.OpenWebsite("www.google.com")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Succeeded);
        Assert.StartsWith("o navegador não abriu", result.Detail);
    }

    [Fact]
    public async Task OpenWebsite_WithoutAnOpener_IsRefused()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.OpenWebsite("www.google.com")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((false, "esta máquina não abre sites"), result);
    }

    [Fact]
    public async Task OpenProgram_StartsItAsTheStudent()
    {
        var starter = new RecordingProgramStarter();
        using var harness = new ServerHarness(new CountingScreenCapture(), programStarter: starter);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.OpenProgram(@"""C:\Program Files\Programa\app.exe"" -a ""b c"""))
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((true, "aberto"), result);
        Assert.Equal(new[] { (@"C:\Program Files\Programa\app.exe", @"-a ""b c""") }, starter.Started);
    }

    // The student's own %USERPROFILE%, not the teacher's: expanded where it runs.
    [Fact]
    public async Task OpenProgram_ExpandsVariablesOnTheStudentsMachine()
    {
        Environment.SetEnvironmentVariable("MIDVIEW_TESTE_PASTA", @"C:\Users\aluno");
        var starter = new RecordingProgramStarter();
        using var harness = new ServerHarness(new CountingScreenCapture(), programStarter: starter);
        using var teacher = harness.ConnectMaster();

        await Task.Run(() => teacher.OpenProgram(@"notepad %MIDVIEW_TESTE_PASTA%\aula.txt")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { ("notepad", @"C:\Users\aluno\aula.txt") }, starter.Started);
    }

    [Fact]
    public async Task OpenProgram_NotFound_SaysWhich()
    {
        var starter = new RecordingProgramStarter { Failure = new System.ComponentModel.Win32Exception(2) };
        using var harness = new ServerHarness(new CountingScreenCapture(), programStarter: starter);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.OpenProgram("progamaerrado -x")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((false, "programa não encontrado: progamaerrado"), result);
    }

    [Fact]
    public async Task OpenProgram_StillWaiting_SaysSo()
    {
        var starter = new RecordingProgramStarter { Finishes = false };
        using var harness = new ServerHarness(new CountingScreenCapture(), programStarter: starter);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.OpenProgram("instalador.exe")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Succeeded);
        Assert.Contains("administrador", result.Detail);
    }

    [Fact]
    public async Task OpenProgram_NotACommand_IsRefused_AndNothingStarts()
    {
        var starter = new RecordingProgramStarter();
        using var harness = new ServerHarness(new CountingScreenCapture(), programStarter: starter);
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.OpenProgram("\"sem fim")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((false, "comando inválido"), result);
        Assert.Empty(starter.Started);
    }

    [Fact]
    public async Task OpenProgram_WithoutAStarter_IsRefused()
    {
        using var harness = new ServerHarness(new CountingScreenCapture());
        using var teacher = harness.ConnectMaster();

        var result = await Task.Run(() => teacher.OpenProgram("powershell")).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal((false, "esta máquina não abre programas"), result);
    }

    private static void WriteFile(string root, string relativePath, byte[] content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private sealed class TempInbox : IDisposable
    {
        public TempInbox()
        {
            Directory = Path.Combine(Path.GetTempPath(), $"midview-inbox-{Guid.NewGuid():N}");
            Inbox = new FileInbox(Directory);
        }

        public string Directory { get; }

        public FileInbox Inbox { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition");
            await Task.Delay(20);
        }
    }

    // A Master that only watches the screen and goes away, as a closed
    // "Ver a tela" window does (it sends StopControl as it closes, whether it
    // controlled or not). Returns once the Session has finished with its
    // connection, its last clean-up included: the serving slot is that
    // connection's until then, so a frame for the next Master means it is over.
    private static async Task WatchAndLeave(ServerHarness harness, bool stopControlOnTheWayOut)
    {
        var watching = harness.ConnectMaster();
        watching.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        watching.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
        await Task.Run(() => watching.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(5)); // it is being served
        if (stopControlOnTheWayOut)
        {
            watching.Channel.Send(new Message(MessageType.StopControl, Array.Empty<byte>()));
        }
        watching.Dispose();

        using var next = harness.ConnectMaster();
        next.Channel.Send(new Message(MessageType.ScreenStream, Array.Empty<byte>()));
        next.Channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
        await Task.Run(() => next.Channel.Receive()).WaitAsync(TimeSpan.FromSeconds(10));
    }

    // Stands still until a test moves it, and counts how often it was read, so
    // a test can wait for the Session to have looked at the time (a heartbeat
    // arriving) without sleeping.
    private sealed class SteppedClock : ISystemClock
    {
        private readonly object _gate = new();
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        private int _reads;

        public int Reads
        {
            get
            {
                lock (_gate)
                {
                    return _reads;
                }
            }
        }

        public DateTimeOffset UtcNow
        {
            get
            {
                lock (_gate)
                {
                    _reads++;
                    return _now;
                }
            }
        }

        public void Advance(TimeSpan by)
        {
            lock (_gate)
            {
                _now += by;
            }
        }
    }

    private sealed class ServerHarness : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopServer = new();
        private readonly Task _serverTask;
        private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _serverCert =
            SelfSignedCertificate.Create("session-under-test");
        private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _masterCert =
            SelfSignedCertificate.Create("master-under-test");

        public ServerHarness(
            IScreenCapture capture, IMessageDisplay? messageDisplay = null, IChatWindows? chatWindows = null,
            FileInbox? fileInbox = null, IAppCloser? appCloser = null, DeliveryFolder? deliveryFolder = null,
            IOpenProgramsLister? openPrograms = null, IScreenLockDisplay? screenLock = null,
            IFileOpener? fileOpener = null, IRecycleBin? recycleBin = null, string? trustedMastersFile = null,
            ICountdownNotice? countdownNotice = null, IWebsiteOpener? websiteOpener = null,
            IProgramStarter? programStarter = null, TimeSpan? frameAckTimeout = null, ClassFolder? classFolder = null)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            InputLock = new InputLockManager(Devices, Clock, TimeSpan.FromSeconds(5));

            var server = new SessionServer(
                _listener, _serverCert,
                trustedMastersFile is null ? new TrustStore(new[] { _masterCert.Thumbprint }) : TrustStore.FromFile(trustedMastersFile),
                capture, Injector,
                InputLock,
                messageDisplay, chatWindows, fileInbox, appCloser, deliveryFolder, openPrograms, screenLock,
                fileOpener, recycleBin, countdownNotice, websiteOpener, programStarter, frameAckTimeout,
                classFolder: classFolder);
            _serverTask = Task.Run(() => server.Run(_stopServer.Token));
        }

        public int Port { get; }

        public string MasterThumbprint => _masterCert.Thumbprint;

        // The student's keyboard and mouse, as the input lock sees them.
        public FakeInputDeviceController Devices { get; } = new();

        // The lock the Session uses, and the time it measures heartbeats by.
        public SteppedClock Clock { get; } = new();

        public InputLockManager InputLock { get; }

        public FakeInputInjector Injector { get; } = new();

        public ConnectedMaster ConnectMaster()
        {
            var tcpClient = new TcpClient();
            tcpClient.Connect(IPAddress.Loopback, Port);
            var channel = SecureChannel.AuthenticateAsClient(
                tcpClient, "session-under-test", _masterCert, new TrustStore(new[] { _serverCert.Thumbprint }));
            return new ConnectedMaster(tcpClient, channel);
        }

        public void Dispose()
        {
            _stopServer.Cancel();
            _listener.Stop(); // unblocks AcceptTcpClient() with an expected exception, as in the test above
            try
            {
                _serverTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }
            _serverCert.Dispose();
            _masterCert.Dispose();
        }
    }

    private sealed class ConnectedMaster : IDisposable
    {
        private readonly TcpClient _tcpClient;

        public ConnectedMaster(TcpClient tcpClient, SecureChannel channel)
        {
            _tcpClient = tcpClient;
            Channel = channel;
        }

        public SecureChannel Channel { get; }

        // What a Master from before ScreenStream sends as soon as it opens to
        // control, so the tests built on it cover the FrameTile stream; the
        // current one sends ScreenStream first (see the ScreenStream_ tests).
        public void StartControl() => Channel.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));

        public Message ShowMessage(string text)
        {
            Channel.Send(new Message(MessageType.ShowMessage, TextMessage.Encode(text)));
            return Channel.Receive();
        }

        public (bool Succeeded, string Detail) OpenProgram(string command)
        {
            Channel.Send(new Message(MessageType.OpenProgram, TextMessage.Encode(command)));
            return CommandResult.Decode(Channel.Receive().Payload);
        }

        public (bool Succeeded, string Detail) OpenWebsite(string address)
        {
            Channel.Send(new Message(MessageType.OpenWebsite, TextMessage.Encode(address)));
            return CommandResult.Decode(Channel.Receive().Payload);
        }

        public (bool Succeeded, string Detail) ClearDeliveryFolder()
        {
            Channel.Send(new Message(MessageType.ClearDeliveryFolder, Array.Empty<byte>()));
            return CommandResult.Decode(Channel.Receive().Payload);
        }

        public ((bool Saved, string Detail) Accepted, (bool Saved, string Detail) Result) SendFile(
            string name, byte[] content, SendFileOptions? options = null)
        {
            Channel.Send(options is null
                ? new Message(MessageType.SendFile, FileTransfer.EncodeHeader(name, content.Length))
                : new Message(MessageType.SendFileWithOptions, FileTransfer.EncodeOptionsHeader(options, name, content.Length)));
            var accepted = CommandResult.Decode(Channel.Receive().Payload);
            for (var offset = 0; offset < content.Length; offset += FileTransfer.ChunkSize)
            {
                var length = Math.Min(FileTransfer.ChunkSize, content.Length - offset);
                Channel.Send(new Message(MessageType.FileChunk, content.AsSpan(offset, length).ToArray()));
            }
            return (accepted, CommandResult.Decode(Channel.Receive().Payload));
        }

        // Every file the Session sent (relative path -> content), then how it ended.
        public (Dictionary<string, byte[]> Files, (bool Succeeded, string Detail) Result) CollectFiles()
        {
            Channel.Send(new Message(MessageType.CollectFiles, Array.Empty<byte>()));
            var files = new Dictionary<string, byte[]>();
            while (true)
            {
                var message = Channel.Receive();
                if (message.Type == MessageType.CommandAck)
                {
                    return (files, CommandResult.Decode(message.Payload));
                }
                Assert.Equal(MessageType.SendFile, message.Type);
                Assert.True(FileTransfer.TryDecodeHeader(message.Payload, out var path, out var size));
                var content = new List<byte>();
                while (content.Count < size)
                {
                    var chunk = Channel.Receive();
                    Assert.Equal(MessageType.FileChunk, chunk.Type);
                    content.AddRange(chunk.Payload);
                }
                Assert.Equal(size, content.Count);
                files.Add(path, content.ToArray());
            }
        }

        public (Guid CurrentLessonId, string? Problem, IReadOnlyList<ClassFolderEntry> Entries) ListClassFolder()
        {
            var parts = ListClassFolderInParts();
            return (parts[0].CurrentLessonId, parts[0].Problem, parts.SelectMany(part => part.Entries).ToList());
        }

        // Every ClassFolderListing message, up to the one that says it is the last.
        public List<(Guid CurrentLessonId, string? Problem, IReadOnlyList<ClassFolderEntry> Entries, bool More)> ListClassFolderInParts()
        {
            Channel.Send(new Message(MessageType.ListClassFolder, Array.Empty<byte>()));
            var parts = new List<(Guid, string?, IReadOnlyList<ClassFolderEntry>, bool)>();
            bool more;
            do
            {
                var reply = Channel.Receive();
                Assert.Equal(MessageType.ClassFolderListing, reply.Type);
                Assert.True(ClassFolderWire.TryDecodeListing(reply.Payload, out var current, out var problem, out var entries, out more));
                parts.Add((current, problem, entries, more));
            }
            while (more);
            return parts;
        }

        // The file the Session sent for a PullClassFile (its name as the header
        // gives it, and its content), or null for both when it only answered;
        // then how that ended.
        public (string? Name, byte[]? Content, (bool Succeeded, string Detail) Result) PullClassFile(Guid lessonId, string path)
        {
            Channel.Send(new Message(MessageType.PullClassFile, ClassFolderWire.EncodeFileRef(lessonId, path)));
            var first = Channel.Receive();
            if (first.Type == MessageType.CommandAck)
            {
                return (null, null, CommandResult.Decode(first.Payload));
            }

            Assert.Equal(MessageType.SendFile, first.Type);
            Assert.True(FileTransfer.TryDecodeHeader(first.Payload, out var name, out var size));
            var content = new List<byte>();
            while (content.Count < size)
            {
                var chunk = Channel.Receive();
                Assert.Equal(MessageType.FileChunk, chunk.Type);
                content.AddRange(chunk.Payload);
            }
            Assert.Equal(size, content.Count);
            return (name, content.ToArray(), CommandResult.Decode(Channel.Receive().Payload));
        }

        // Accepted or refused first, then (if accepted) how saving it ended; a
        // refusal has no second answer (Result is default). replacing: the
        // version of the file at that path the push may replace (none: null).
        public ((bool Succeeded, string Detail) Accepted, (bool Succeeded, string Detail) Result) PushClassFile(
            Guid lessonId, string path, byte[] content, long lastWriteTicks, (long Size, long LastWriteTicks)? replacing = null)
        {
            Channel.Send(new Message(
                MessageType.PushClassFile, ClassFolderWire.EncodePushHeader(lessonId, path, content.Length, lastWriteTicks, replacing)));
            var accepted = CommandResult.Decode(Channel.Receive().Payload);
            if (!accepted.Succeeded)
            {
                return (accepted, default);
            }

            for (var offset = 0; offset < content.Length; offset += FileTransfer.ChunkSize)
            {
                var length = Math.Min(FileTransfer.ChunkSize, content.Length - offset);
                Channel.Send(new Message(MessageType.FileChunk, content.AsSpan(offset, length).ToArray()));
            }
            return (accepted, CommandResult.Decode(Channel.Receive().Payload));
        }

        // One CommandAck back, whatever the message.
        public (bool Succeeded, string Detail) Command(MessageType type, byte[] payload)
        {
            Channel.Send(new Message(type, payload));
            return CommandResult.Decode(Channel.Receive().Payload);
        }

        public (bool Succeeded, string Detail) CloseApps(CloseAppsMode mode, int delaySeconds = 0)
        {
            Channel.Send(new Message(MessageType.CloseApps, CloseAppsCommand.Encode(mode, delaySeconds)));
            return CommandResult.Decode(Channel.Receive().Payload);
        }

        // Opens a screen-lock connection; it stays locked until disposed.
        public (bool Succeeded, string Detail) LockScreen()
        {
            Channel.Send(new Message(MessageType.LockScreen, Array.Empty<byte>()));
            return CommandResult.Decode(Channel.Receive().Payload);
        }

        public Message RequestThumbnail() => RequestThumbnail(Array.Empty<byte>());

        public Message RequestThumbnail(byte[] payload)
        {
            Channel.Send(new Message(MessageType.RequestThumbnail, payload));
            return Channel.Receive();
        }

        public void Dispose()
        {
            Channel.Dispose();
            _tcpClient.Dispose();
        }
    }
}
