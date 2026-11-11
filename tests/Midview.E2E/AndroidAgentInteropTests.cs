using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;
using Xunit.Abstractions;

// The real Master code (PairingSession, ThumbnailPoller, CommandClient,
// DiscoveryClient) against the Android agent running on a phone or an
// emulator. Only with MIDVIEW_ANDROID_HOST (the phone's address) set:
//   dotnet test tests/Midview.E2E --filter FullyQualifiedName~AndroidAgentInterop
// Optional: MIDVIEW_ANDROID_AGENT_PORT / MIDVIEW_ANDROID_SESSION_PORT, when
// the phone is reached through "adb forward" (the search, over UDP, is then
// left out); MIDVIEW_ANDROID_ADB and MIDVIEW_ANDROID_SERIAL, to also check
// that the phone shows the same code as the teacher.
// The teacher is a throwaway certificate of these tests, kept in the temp
// folder so a second run is the same teacher (a phone that already has a
// teacher refuses any other): never the real Master's.
public sealed class AndroidAgentInteropTests(ITestOutputHelper output)
{
    private static readonly string? Host = Environment.GetEnvironmentVariable("MIDVIEW_ANDROID_HOST");
    private static readonly int AgentPort = PortFrom("MIDVIEW_ANDROID_AGENT_PORT", ConnectionClient.AgentPort);
    private static readonly int SessionPort = PortFrom("MIDVIEW_ANDROID_SESSION_PORT", ConnectionClient.SessionPort);

    [AndroidFact]
    public void Master_pairs_with_real_android_agent()
    {
        using var teacher = Teacher();
        using var session = PairingSession.Start(Host!, teacher, "E2E-ANDROID", AgentPort);
        output.WriteLine($"code {session.CodeText}, phone {session.AgentThumbprint}");
        CheckCodeOnPhone(session.CodeText);

        var machine = session.Confirm();

        Assert.Equal(DeviceKind.Android, machine.Kind);
        Assert.True(machine.IsPhone);
        Assert.False(string.IsNullOrWhiteSpace(machine.Name));
        // One certificate for both ports on a phone.
        Assert.Equal(session.AgentThumbprint, machine.SessionThumbprint);
        Assert.Null(machine.MacAddress);
        output.WriteLine($"paired with {machine.Name}");
    }

    [AndroidFact]
    public void Poller_sees_android_agent_online()
    {
        using var teacher = Teacher();
        var machine = Pair(teacher);
        // A standing tile, as the Master asks for a phone (TileSize.PhoneRequest).
        var box = (Width: 90, Height: 195);
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? lastFailure = null;

        using var poller = new ThumbnailPoller(
            machine, teacher, SessionPort, AgentPort, thumbnailSize: () => box);
        poller.ThumbnailReceived += jpeg => received.TrySetResult(jpeg);
        poller.Unreachable += status => lastFailure = status;
        poller.Start();

        Assert.True(received.Task.Wait(TimeSpan.FromSeconds(10)), $"no thumbnail in 10 s (last: {lastFailure ?? "nothing"})");
        var (width, height) = JpegSize(received.Task.Result);
        output.WriteLine($"thumbnail {width}x{height}, {received.Task.Result.Length} bytes");
        Assert.InRange(width, 1, box.Width);
        Assert.InRange(height, 1, box.Height);
        Assert.True(height > width, "the phone's thumbnail stands up");
    }

    [AndroidFact]
    public void Pc_command_gets_the_not_on_phone_answer()
    {
        using var teacher = Teacher();
        var machine = Pair(teacher);

        var (succeeded, detail) = CommandClient.SendCommandForResult(
            Host!, AgentPort, machine.AgentThumbprint!, teacher, MessageType.UnblockInternet, Array.Empty<byte>());

        Assert.False(succeeded);
        Assert.Equal("Ainda não funciona em celular.", detail);
    }

    [AndroidFact]
    public void Untrusted_teacher_gets_no_answer()
    {
        using var stranger = SelfSignedCertificate.Create("midview-e2e-stranger");
        using var teacher = Teacher();
        var phone = Pair(teacher);

        Assert.ThrowsAny<Exception>(() => CommandClient.SendCommandForResult(
            Host!, AgentPort, phone.AgentThumbprint!, stranger, MessageType.UnblockInternet, Array.Empty<byte>()));
    }

    // The big window's stream ("Ver a tela"), as ViewControlWindow opens it.
    // Needs the capture allowed: the student taps "Iniciar agora" on the
    // phone, which asks as this connection comes in (if it was not allowed
    // already). The first update is the whole screen.
    [AndroidFact]
    public void Screen_stream_sends_frames_from_phone()
    {
        using var teacher = Teacher();
        var machine = Pair(teacher);
        using var client = ConnectionClient.Connect(
            Host!, SessionPort, machine.SessionThumbprint!, teacher, TimeSpan.FromSeconds(10));
        var sending = new object();
        lock (sending)
        {
            client.SendScreenStream();
            client.SendHeartbeat();
        }
        output.WriteLine("If the phone asks, tap \"Iniciar agora\" on it.");

        var frames = new System.Collections.Concurrent.BlockingCollection<Message>();
        var receiving = Task.Run(() =>
        {
            try
            {
                while (true) frames.Add(client.ReceiveNext());
            }
            catch (Exception)
            {
                frames.CompleteAdding();
            }
        });
        using var heartbeat = new Timer(_ =>
        {
            try
            {
                lock (sending) client.SendHeartbeat();
            }
            catch (Exception)
            {
                // The test ends the connection.
            }
        }, null, 1000, 1000);

        var watch = Stopwatch.StartNew();
        Assert.True(frames.TryTake(out var first, StreamWait), $"no ScreenFrame in {StreamWait.TotalSeconds} s");
        output.WriteLine($"first update after {watch.Elapsed.TotalSeconds:F1} s");
        Assert.Equal(MessageType.ScreenFrame, first.Type);
        var frame = ScreenFrame.Decode(first.Payload);
        Assert.NotNull(frame);
        Assert.Equal(1, frame!.FrameId);
        Assert.InRange(Math.Max(frame.ScreenWidth, frame.ScreenHeight), 1, 1280);
        Assert.Equal((long)frame.ScreenWidth * frame.ScreenHeight, frame.Regions.Sum(r => (long)r.Width * r.Height));
        foreach (var region in frame.Regions)
        {
            Assert.Equal((region.Width, region.Height), JpegSize(region.Jpeg));
        }
        output.WriteLine($"screen {frame.ScreenWidth}x{frame.ScreenHeight}, {frame.Regions.Count} regions");
        lock (sending) client.SendFrameAck(frame.FrameId);

        // Acknowledged and heard from every second, the stream stays open.
        Thread.Sleep(TimeSpan.FromSeconds(3));
        Assert.False(frames.IsAddingCompleted, "the phone closed the stream");
        lock (sending) client.SendStopControl();
        client.Dispose();
        receiving.Wait(TimeSpan.FromSeconds(5));
    }

    // Long enough for the student to tap "Iniciar agora"; with the capture
    // already allowed the first update comes at once.
    private static readonly TimeSpan StreamWait = TimeSpan.FromSeconds(90);

    // "Bloquear telas" as the Master keeps it (ScreenLockKeeper). Only a
    // phone made Device Owner by the Master's QR code can be locked; a test
    // phone that is not one answers why, and nothing on it changes.
    [AndroidFact]
    public void Lock_on_a_phone_that_is_not_device_owner_is_refused()
    {
        using var teacher = Teacher();
        var machine = Pair(teacher);
        var status = new TaskCompletionSource<(bool Locked, string Detail)>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var keeper = new ScreenLockKeeper(machine, teacher, SessionPort))
        {
            keeper.StatusChanged += (locked, detail) => status.TrySetResult((locked, detail));
            keeper.Start();
            Assert.True(status.Task.Wait(TimeSpan.FromSeconds(15)), "no answer to LockScreen in 15 s");
        }

        output.WriteLine($"lock: {status.Task.Result}");
        Assert.Equal((false, "este celular não bloqueia a tela"), status.Task.Result);
    }

    [AndroidLanFact]
    public void Search_finds_the_phone()
    {
        using var teacher = Teacher();
        var machine = Pair(teacher);

        var found = DiscoveryClient.Search(
            teacher.Thumbprint, new[] { new IPEndPoint(IPAddress.Parse(Host!), Discovery.Port) }, TimeSpan.FromSeconds(2));

        var phone = Assert.Single(found, f => f.AgentThumbprint == machine.AgentThumbprint);
        Assert.Equal(DeviceKind.Android, phone.Kind);
        Assert.Equal(machine.Name, phone.Name);
        Assert.Equal(TeacherStatus.You, phone.Teachers);
        output.WriteLine($"found {phone.Name} at {phone.Address}");
    }

    private KnownMachine Pair(X509Certificate2 teacher)
    {
        using var session = PairingSession.Start(Host!, teacher, "E2E-ANDROID", AgentPort);
        return session.Confirm();
    }

    // The same throwaway teacher on every run.
    private static X509Certificate2 Teacher()
    {
        var folder = Path.Combine(Path.GetTempPath(), "midview-android-e2e");
        var pfx = Path.Combine(folder, "teacher.pfx");
        if (!File.Exists(pfx))
        {
            Directory.CreateDirectory(folder);
            using var created = SelfSignedCertificate.Create("midview-e2e-android-teacher");
            File.WriteAllBytes(pfx, created.Export(X509ContentType.Pfx));
        }
        return new X509Certificate2(File.ReadAllBytes(pfx), (string?)null, X509KeyStorageFlags.Exportable);
    }

    // The phone's pairing notification carries the code it shows.
    private void CheckCodeOnPhone(string code)
    {
        var adb = Environment.GetEnvironmentVariable("MIDVIEW_ANDROID_ADB");
        var serial = Environment.GetEnvironmentVariable("MIDVIEW_ANDROID_SERIAL");
        if (string.IsNullOrEmpty(adb) || string.IsNullOrEmpty(serial))
        {
            output.WriteLine("no adb: the code on the phone was not looked at");
            return;
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            var start = new ProcessStartInfo(adb)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                ArgumentList = { "-s", serial, "shell", "dumpsys", "notification", "--noredact" },
            };
            using var process = Process.Start(start)!;
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (text.Contains(code))
            {
                output.WriteLine($"the phone shows {code}");
                return;
            }
            Assert.True(DateTime.UtcNow < deadline, $"the phone does not show {code}");
            Thread.Sleep(500);
        }
    }

    // Width and height from a baseline or progressive JPEG's frame header.
    private static (int Width, int Height) JpegSize(byte[] jpeg)
    {
        Assert.True(jpeg.Length > 4 && jpeg[0] == 0xFF && jpeg[1] == 0xD8, "not a JPEG");
        var at = 2;
        while (at + 9 < jpeg.Length)
        {
            Assert.Equal(0xFF, jpeg[at]);
            var marker = jpeg[at + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 2));
            if (marker is >= 0xC0 and <= 0xC2)
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 7)), BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 5)));
            }
            at += 2 + length;
        }
        throw new InvalidDataException("JPEG without a frame header");
    }

    private static int PortFrom(string variable, int standard) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out var port) ? port : standard;

    internal static bool PortsForwarded =>
        Environment.GetEnvironmentVariable("MIDVIEW_ANDROID_AGENT_PORT") is { Length: > 0 }
        || Environment.GetEnvironmentVariable("MIDVIEW_ANDROID_SESSION_PORT") is { Length: > 0 };
}

// A test that needs a running Android agent (MIDVIEW_ANDROID_HOST).
public class AndroidFactAttribute : FactAttribute
{
    public AndroidFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MIDVIEW_ANDROID_HOST")))
        {
            Skip = "MIDVIEW_ANDROID_HOST is not set: no Android agent to test against";
        }
    }
}

// One that also needs to reach the phone over UDP: not through adb forward.
public sealed class AndroidLanFactAttribute : AndroidFactAttribute
{
    public AndroidLanFactAttribute()
    {
        if (Skip is null && AndroidAgentInteropTests.PortsForwarded)
        {
            Skip = "the phone is reached through adb forward: the search (UDP) can't be";
        }
    }
}
