using Midview.Master;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class ChatConnectionTests
{
    [Fact]
    public async Task TeacherAndStudentLines_ReachTheOtherSide()
    {
        using var certificates = new Certificates();
        var chats = new RecordingChatWindows();
        using var session = new ThumbnailPollerTests.LoopbackSession(
            0, certificates.Session, certificates.Master, chatWindows: chats);
        var studentLine = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var chat = await Task.Run(() => ChatConnection.Open(
            Machine(certificates), certificates.Master, session.Port)).WaitAsync(TimeSpan.FromSeconds(10));
        chat.StudentMessageReceived += text => studentLine.TrySetResult(text);
        chat.Start();

        Assert.True(chat.Send("Terminou o exercício?"));
        await WaitUntil(() => chats.Window?.TeacherLines.Count == 1);
        Assert.Equal("Terminou o exercício?", chats.Window!.TeacherLines[0]);

        Assert.True(chats.Window.StudentTypes("Quase!"));
        Assert.Equal("Quase!", await studentLine.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Dispose_EndsTheConversationOnTheStudentsSide()
    {
        using var certificates = new Certificates();
        var chats = new RecordingChatWindows();
        using var session = new ThumbnailPollerTests.LoopbackSession(
            0, certificates.Session, certificates.Master, chatWindows: chats);
        var chat = await Task.Run(() => ChatConnection.Open(
            Machine(certificates), certificates.Master, session.Port)).WaitAsync(TimeSpan.FromSeconds(10));
        chat.Start();
        await WaitUntil(() => chats.Window is not null);

        chat.Dispose();

        await WaitUntil(() => chats.Window!.Ended);
    }

    [Fact]
    public async Task StudentSideClosesTheConnection_RaisesClosed()
    {
        using var certificates = new Certificates();
        // A minimal stand-in for a Session whose student logs off: it accepts
        // the chat, then closes its end of the connection.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var fakeSession = Task.Run(() =>
        {
            using var tcpClient = listener.AcceptTcpClient();
            using var channel = SecureChannel.AuthenticateAsServer(
                tcpClient, certificates.Session, new TrustStore(new[] { certificates.Master.Thumbprint }));
            Assert.Equal(MessageType.ChatOpen, channel.Receive().Type);
        });

        var closed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var chat = await Task.Run(() => ChatConnection.Open(
            Machine(certificates), certificates.Master, port)).WaitAsync(TimeSpan.FromSeconds(10));
        chat.Closed += reason => closed.TrySetResult(reason);
        chat.Start();
        await fakeSession.WaitAsync(TimeSpan.FromSeconds(10));
        listener.Stop();

        Assert.False(string.IsNullOrEmpty(await closed.Task.WaitAsync(TimeSpan.FromSeconds(10))));
    }

    private static KnownMachine Machine(Certificates certificates) =>
        new("Aluno-1", "127.0.0.1", certificates.Session.Thumbprint, null);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition");
            await Task.Delay(20);
        }
    }

    private sealed class Certificates : IDisposable
    {
        public System.Security.Cryptography.X509Certificates.X509Certificate2 Master { get; } =
            SelfSignedCertificate.Create("master-under-test");

        public System.Security.Cryptography.X509Certificates.X509Certificate2 Session { get; } =
            SelfSignedCertificate.Create("session-under-test");

        public void Dispose()
        {
            Master.Dispose();
            Session.Dispose();
        }
    }

    internal sealed class RecordingChatWindows : IChatWindows
    {
        public RecordingWindow? Window { get; private set; }

        public IChatWindow Open(Func<string, bool> sendToTeacher)
        {
            Window = new RecordingWindow(sendToTeacher);
            return Window;
        }
    }

    internal sealed class RecordingWindow : IChatWindow
    {
        private readonly List<string> _teacherLines = new();

        public RecordingWindow(Func<string, bool> sendToTeacher)
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
}
