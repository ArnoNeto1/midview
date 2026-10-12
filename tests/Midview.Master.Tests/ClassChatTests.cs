using System.Collections.Concurrent;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Midview.Session;
using Xunit;

public class ClassChatTests
{
    [Fact]
    public async Task ConnectAsync_OpensTheChatOnEveryMachine()
    {
        var fakes = new FakeConnections();
        using var chat = new ClassChat(Machines("Aluno-1", "Aluno-2", "Aluno-3"), fakes.Open);

        await chat.ConnectAsync();

        Assert.Equal(new[] { "Aluno-1", "Aluno-2", "Aluno-3" }, fakes.Opened.Keys.Order());
        Assert.All(fakes.Opened.Values, connection => Assert.True(connection.Started));
        Assert.Equal(3, chat.ConnectedCount);
        Assert.Equal("Conectado a 3 de 3 máquinas.", chat.Status);
    }

    [Fact]
    public async Task WhileConnecting_TheStatusSaysHowManyAreStillComing()
    {
        var fakes = new FakeConnections();
        var aluno2 = fakes.Hold("Aluno-2");
        var aluno3 = fakes.Hold("Aluno-3");
        using var chat = new ClassChat(Machines("Aluno-1", "Aluno-2", "Aluno-3"), fakes.Open);
        Assert.Equal("Conectando a 3 máquinas...", chat.Status);

        var connecting = chat.ConnectAsync();
        await WaitUntil(() => chat.ConnectedCount == 1);
        Assert.Equal("Conectado a 1 de 3 máquinas. Aguardando 2.", chat.Status);

        aluno2.Set();
        aluno3.Set();
        await connecting;
        Assert.Equal("Conectado a 3 de 3 máquinas.", chat.Status);
    }

    [Fact]
    public async Task OneMachine_IsSaidInTheSingular()
    {
        using var chat = new ClassChat(Machines("Aluno-1"), new FakeConnections().Open);

        await chat.ConnectAsync();

        Assert.Equal("Conectado a 1 de 1 máquina.", chat.Status);
    }

    [Fact]
    public async Task TheTeachersLine_GoesToEveryConnectedMachine()
    {
        var fakes = new FakeConnections();
        using var chat = new ClassChat(Machines("Aluno-1", "Aluno-2", "Aluno-3"), fakes.Open);
        await chat.ConnectAsync();

        Assert.Equal(3, await chat.SendAsync("Abram o exercício 2"));

        Assert.All(fakes.Opened.Values, connection => Assert.Equal(new[] { "Abram o exercício 2" }, connection.TeacherLines));
    }

    [Fact]
    public async Task AStudentsReply_ComesBackWithTheirMachinesName()
    {
        var fakes = new FakeConnections();
        using var chat = new ClassChat(Machines("Aluno-1", "Aluno-2"), fakes.Open);
        var lines = new List<string>();
        chat.LineReceived += lines.Add;
        await chat.ConnectAsync();

        fakes.Opened["Aluno-2"].StudentTypes("terminei");

        Assert.Equal(new[] { "Aluno-2: terminei" }, lines);
    }

    // Only the teacher sees what a student writes.
    [Fact]
    public async Task AStudentsReply_IsNotSentToTheOtherStudents()
    {
        var fakes = new FakeConnections();
        using var chat = new ClassChat(Machines("Aluno-1", "Aluno-2"), fakes.Open);
        await chat.ConnectAsync();

        fakes.Opened["Aluno-2"].StudentTypes("terminei");

        Assert.All(fakes.Opened.Values, connection => Assert.Empty(connection.TeacherLines));
    }

    [Fact]
    public async Task AMachineThatCantConnect_IsListedWithTheReason()
    {
        var fakes = new FakeConnections();
        fakes.Unreachable["Aluno-2"] = new SocketException((int)SocketError.ConnectionRefused);
        using var chat = new ClassChat(Machines("Aluno-1", "Aluno-2", "Aluno-3"), fakes.Open);

        await chat.ConnectAsync();

        Assert.Equal(2, chat.ConnectedCount);
        Assert.Equal("Conectado a 2 de 3 máquinas. Não conectou: Aluno-2 (Sem usuário logado).", chat.Status);
        Assert.Equal(2, await chat.SendAsync("oi"));
    }

    [Fact]
    public async Task AMachineThatLeaves_IsDroppedAndListed()
    {
        var fakes = new FakeConnections();
        using var chat = new ClassChat(Machines("Aluno-1", "Aluno-2", "Aluno-3"), fakes.Open);
        await chat.ConnectAsync();
        var changes = 0;
        chat.Changed += () => changes++;

        fakes.Opened["Aluno-1"].Leave("o aluno saiu ou a conexão caiu");

        Assert.Equal(1, changes);
        Assert.Equal("Conectado a 2 de 3 máquinas. Saíram: Aluno-1.", chat.Status);
        Assert.Equal(2, await chat.SendAsync("oi"));
        Assert.Empty(fakes.Opened["Aluno-1"].TeacherLines);
    }

    [Fact]
    public async Task AMachineTheLineCantReach_IsDropped()
    {
        var fakes = new FakeConnections();
        using var chat = new ClassChat(Machines("Aluno-1", "Aluno-2", "Aluno-3"), fakes.Open);
        await chat.ConnectAsync();
        fakes.Opened["Aluno-3"].Broken = true;

        Assert.Equal(2, await chat.SendAsync("oi"));

        Assert.Equal("Conectado a 2 de 3 máquinas. Saíram: Aluno-3.", chat.Status);
    }

    [Fact]
    public async Task NobodyLeft_SaysHowToReconnect()
    {
        var fakes = new FakeConnections();
        fakes.Unreachable["Aluno-2"] = new SocketException((int)SocketError.TimedOut);
        using var chat = new ClassChat(Machines("Aluno-1", "Aluno-2"), fakes.Open);
        await chat.ConnectAsync();

        fakes.Opened["Aluno-1"].Leave("o aluno saiu ou a conexão caiu");

        Assert.Equal(0, chat.ConnectedCount);
        Assert.Equal(
            "Conectado a 0 de 2 máquinas. Não conectou: Aluno-2 (Offline). Saíram: Aluno-1. Feche e abra de novo para reconectar.",
            chat.Status);
        Assert.Equal(0, await chat.SendAsync("oi"));
    }

    [Fact]
    public async Task Names_AreListedInTheRoomsOrder()
    {
        var fakes = new FakeConnections();
        fakes.Unreachable["Aluno-10"] = new SocketException((int)SocketError.TimedOut);
        fakes.Unreachable["Aluno-2"] = new SocketException((int)SocketError.TimedOut);
        using var chat = new ClassChat(Machines("Aluno-10", "Aluno-2", "Aluno-3"), fakes.Open);

        await chat.ConnectAsync();

        Assert.Equal("Conectado a 1 de 3 máquinas. Não conectou: Aluno-2 (Offline), Aluno-10 (Offline).", chat.Status);
    }

    [Fact]
    public async Task Lines_ReachEachMachineInTheOrderTheyWereTyped()
    {
        var fakes = new FakeConnections();
        using var chat = new ClassChat(Machines("Aluno-1"), fakes.Open);
        await chat.ConnectAsync();
        var slow = fakes.Opened["Aluno-1"];
        slow.HeldLine = "primeira";

        var first = chat.SendAsync("primeira");
        var second = chat.SendAsync("segunda");
        await WaitUntil(() => slow.Holding);
        slow.SendReleased.Set();
        await Task.WhenAll(first, second);

        Assert.Equal(new[] { "primeira", "segunda" }, slow.TeacherLines);
    }

    [Fact]
    public async Task Dispose_EndsTheConversationOnEveryMachine()
    {
        var fakes = new FakeConnections();
        var chat = new ClassChat(Machines("Aluno-1", "Aluno-2", "Aluno-3"), fakes.Open);
        await chat.ConnectAsync();
        var changes = 0;
        chat.Changed += () => changes++;

        chat.Dispose();

        Assert.All(fakes.Opened.Values, connection => Assert.True(connection.Disposed));
        // The teacher ending it isn't the machines leaving.
        Assert.Equal(0, changes);
    }

    // The teacher closed the window before every machine had answered.
    [Fact]
    public async Task AMachineThatConnectsAfterDispose_IsClosedAtOnce()
    {
        var fakes = new FakeConnections();
        var aluno2 = fakes.Hold("Aluno-2");
        var chat = new ClassChat(Machines("Aluno-1", "Aluno-2"), fakes.Open);
        var connecting = chat.ConnectAsync();
        await WaitUntil(() => chat.ConnectedCount == 1);

        chat.Dispose();
        aluno2.Set();
        await connecting;

        Assert.True(fakes.Opened["Aluno-2"].Disposed);
        Assert.False(fakes.Opened["Aluno-2"].Started);
    }

    // Against the real Session code: the class chat needs nothing new on the
    // students' side.
    [Fact]
    public async Task WithRealSessions_EveryStudentGetsTheLine_AndTheTeacherGetsTheReply()
    {
        using var sessionCertificate = SelfSignedCertificate.Create("session-under-test");
        using var masterCertificate = SelfSignedCertificate.Create("master-under-test");
        var windows1 = new ChatConnectionTests.RecordingChatWindows();
        var windows2 = new ChatConnectionTests.RecordingChatWindows();
        using var session1 = new ThumbnailPollerTests.LoopbackSession(
            0, sessionCertificate, masterCertificate, chatWindows: windows1);
        using var session2 = new ThumbnailPollerTests.LoopbackSession(
            0, sessionCertificate, masterCertificate, chatWindows: windows2);
        var ports = new Dictionary<string, int> { ["Aluno-1"] = session1.Port, ["Aluno-2"] = session2.Port };
        var machines = new[] { "Aluno-1", "Aluno-2" }
            .Select(name => new KnownMachine(name, "127.0.0.1", sessionCertificate.Thumbprint, null))
            .ToList();
        var replies = new ConcurrentQueue<string>();
        using var chat = new ClassChat(machines, machine => ChatConnection.Open(machine, masterCertificate, ports[machine.Name]));
        chat.LineReceived += replies.Enqueue;

        await chat.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Conectado a 2 de 2 máquinas.", chat.Status);

        Assert.Equal(2, await chat.SendAsync("Abram o exercício 2").WaitAsync(TimeSpan.FromSeconds(10)));
        await WaitUntil(() => windows1.Window?.TeacherLines.Count == 1 && windows2.Window?.TeacherLines.Count == 1);
        Assert.Equal("Abram o exercício 2", windows1.Window!.TeacherLines[0]);
        Assert.Equal("Abram o exercício 2", windows2.Window!.TeacherLines[0]);

        Assert.True(windows2.Window.StudentTypes("terminei"));
        await WaitUntil(() => replies.Count == 1);
        Assert.Equal(new[] { "Aluno-2: terminei" }, replies);

        chat.Dispose();
        await WaitUntil(() => windows1.Window.Ended && windows2.Window.Ended);
    }

    private static List<KnownMachine> Machines(params string[] names) =>
        names.Select(name => new KnownMachine(name, "127.0.0.1", "thumbprint", null)).ToList();

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition");
            await Task.Delay(20);
        }
    }

    // Opens a FakeConnection per machine, from several threads at once.
    private sealed class FakeConnections
    {
        private readonly ConcurrentDictionary<string, FakeConnection> _opened = new();
        private readonly Dictionary<string, ManualResetEventSlim> _held = new();

        public Dictionary<string, Exception> Unreachable { get; } = new();

        public IReadOnlyDictionary<string, FakeConnection> Opened => _opened;

        // Opening this machine waits until the returned event is set.
        public ManualResetEventSlim Hold(string name) => _held[name] = new ManualResetEventSlim();

        public IChatConnection Open(KnownMachine machine)
        {
            if (_held.TryGetValue(machine.Name, out var gate))
            {
                gate.Wait(TimeSpan.FromSeconds(10));
            }
            if (Unreachable.TryGetValue(machine.Name, out var failure))
            {
                throw failure;
            }
            return _opened[machine.Name] = new FakeConnection();
        }
    }

    private sealed class FakeConnection : IChatConnection
    {
        private readonly List<string> _teacherLines = new();
        private int _closed;

        public event Action<string>? StudentMessageReceived;
        public event Action<string>? Closed;

        public bool Started { get; private set; }

        public bool Disposed { get; private set; }

        // Sends fail from now on, as on a connection that dropped.
        public bool Broken { get; set; }

        // Sending this line waits for SendReleased; Holding says it's waiting.
        public string? HeldLine { get; set; }

        public ManualResetEventSlim SendReleased { get; } = new();

        public bool Holding { get; private set; }

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

        public void Start() => Started = true;

        public bool Send(string text)
        {
            if (text == HeldLine)
            {
                Holding = true;
                SendReleased.Wait(TimeSpan.FromSeconds(10));
            }
            if (Broken)
            {
                Leave("a conexão caiu");
                return false;
            }
            lock (_teacherLines)
            {
                _teacherLines.Add(text);
            }
            return true;
        }

        public void StudentTypes(string text) => StudentMessageReceived?.Invoke(text);

        // Like ChatConnection: Closed is raised once, however it ends.
        public void Leave(string reason)
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0)
            {
                Closed?.Invoke(reason);
            }
        }

        public void Dispose()
        {
            Disposed = true;
            Leave("conversa encerrada");
        }
    }
}
