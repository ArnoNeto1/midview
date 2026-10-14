using System.Text;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class PairingServiceTests : IDisposable
{
    private static readonly string TeacherA = new('A', 40);
    private static readonly string TeacherB = new('B', 40);
    private static readonly string TeacherC = new('C', 40);
    private static readonly string AgentThumbprint = new('D', 40);
    private static readonly string SessionThumbprint = new('5', 40);
    private static readonly byte[] MasterNonce = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid():N}.json");
    private readonly FakeDisplay _display = new();
    private DateTime _now = new(2026, 10, 5, 14, 22, 0);

    public void Dispose()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }

    private PairingService NewService() =>
        new(TrustStore.FromFile(_path), _path, _display, () => _now, "ALUNO-07", () => SessionThumbprint);

    private static void Pair(PairingService service, string master, Exchange exchange, string? mac = "00-1A-2B-3C-4D-5E") =>
        service.Handle(master, AgentThumbprint, Encoding.UTF8.GetBytes("PROF-01"), exchange.Receive, exchange.Send, mac);

    private static Exchange Confirming() =>
        new Exchange().Then(MessageType.PairNonce, MasterNonce).Then(MessageType.PairConfirm);

    private void Trust(params string[] teachers) =>
        File.WriteAllText(_path, "[" + string.Join(",", teachers.Select(teacher => $"\"{teacher}\"")) + "]");

    [Fact]
    public void WithNoTeacher_TheFirstConfirmedPairing_IsTrusted()
    {
        var exchange = Confirming();

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairCommit, MessageType.PairReveal, MessageType.PairDone }, exchange.SentTypes);
        var agentNonce = exchange.Sent[1].Payload;
        Assert.True(Pairing.Opens(exchange.Sent[0].Payload, agentNonce));
        Assert.Equal((Pairing.Code(TeacherA, AgentThumbprint, agentNonce, MasterNonce), "PROF-01"), _display.Shown.Single());
        Assert.Equal(0, _display.OpenWindows);
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
        Assert.Equal(new PairingResult("ALUNO-07", SessionThumbprint, "00-1A-2B-3C-4D-5E"), PairingResult.Decode(exchange.Sent[2].Payload));
    }

    [Fact]
    public void WithATeacher_AnotherOne_IsRefused()
    {
        Trust(TeacherA);
        var exchange = Confirming();

        Pair(NewService(), TeacherB, exchange);

        Assert.Equal(new[] { MessageType.PairRefused }, exchange.SentTypes);
        Assert.Contains("já tem professor", Pairing.RefusalText(exchange.Sent[0].Payload));
        Assert.Empty(_display.Shown);
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    [Fact]
    public void InsideTheNewTeacherWindow_OneMoreTeacherPairs_AndTheWindowCloses()
    {
        Trust(TeacherA);
        var service = NewService();

        var (succeeded, detail) = service.AllowNewTeacher();
        Assert.True(succeeded);
        Assert.Equal("liberada até 14:32", detail);
        Assert.True(service.NewTeacherOpen);

        var second = Confirming();
        Pair(service, TeacherB, second);
        Assert.Equal(MessageType.PairDone, second.Sent.Last().Type);
        Assert.Equal(new[] { TeacherA, TeacherB }, TrustedMastersFile.Load(_path));
        Assert.False(service.NewTeacherOpen);

        var third = Confirming();
        Pair(service, TeacherC, third);
        Assert.Equal(new[] { MessageType.PairRefused }, third.SentTypes);
    }

    [Fact]
    public void TheNewTeacherWindow_Expires()
    {
        Trust(TeacherA);
        var service = NewService();
        service.AllowNewTeacher();

        _now += Pairing.NewTeacherWindow + TimeSpan.FromSeconds(1);

        Assert.False(service.NewTeacherOpen);
        var exchange = Confirming();
        Pair(service, TeacherB, exchange);
        Assert.Equal(new[] { MessageType.PairRefused }, exchange.SentTypes);
    }

    [Fact]
    public void ATeacherAlreadyTrusted_MayPairAgain()
    {
        Trust(TeacherA);
        var exchange = Confirming();

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(MessageType.PairDone, exchange.Sent.Last().Type);
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    [Fact]
    public void Cancelling_TrustsNothing_AndTakesTheCodeDown()
    {
        var exchange = new Exchange().Then(MessageType.PairNonce, MasterNonce).Then(MessageType.PairCancel);

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairCommit, MessageType.PairReveal }, exchange.SentTypes);
        Assert.Single(_display.Shown);
        Assert.Equal(0, _display.OpenWindows);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void ADroppedConnection_TrustsNothing_TakesTheCodeDown_AndFreesTheMachine()
    {
        var service = NewService();
        var dropped = new Exchange().Then(MessageType.PairNonce, MasterNonce);

        Assert.Throws<IOException>(() => Pair(service, TeacherA, dropped));

        Assert.Equal(0, _display.OpenWindows);
        Assert.False(File.Exists(_path));
        var again = Confirming();
        Pair(service, TeacherA, again);
        Assert.Equal(MessageType.PairDone, again.Sent.Last().Type);
    }

    [Fact]
    public void AConfirmationTooLate_IsRefused()
    {
        var exchange = new Exchange()
            .Then(MessageType.PairNonce, MasterNonce)
            .Then(() =>
            {
                _now += Pairing.ConfirmTimeout + TimeSpan.FromSeconds(1);
                return new Message(MessageType.PairConfirm, Array.Empty<byte>());
            });

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairCommit, MessageType.PairReveal, MessageType.PairRefused }, exchange.SentTypes);
        Assert.Contains("tempo", Pairing.RefusalText(exchange.Sent[2].Payload));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void NobodyLoggedOn_IsRefused()
    {
        _display.SomeoneLoggedOn = false;
        var exchange = Confirming();

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairRefused }, exchange.SentTypes);
        Assert.Contains("Ninguém está usando esta máquina", Pairing.RefusalText(exchange.Sent[0].Payload));
    }

    [Fact]
    public void ACodeThatCantBeShown_IsRefused()
    {
        _display.Fails = true;
        var exchange = Confirming();

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairCommit, MessageType.PairRefused }, exchange.SentTypes);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void OnePairingAtATime()
    {
        var service = NewService();
        var second = new Exchange();
        var first = new Exchange()
            .Then(MessageType.PairNonce, MasterNonce)
            .Then(() =>
            {
                Pair(service, TeacherB, second);
                return new Message(MessageType.PairConfirm, Array.Empty<byte>());
            });

        Pair(service, TeacherA, first);

        Assert.Equal(new[] { MessageType.PairRefused }, second.SentTypes);
        Assert.Contains("Outro pareamento", Pairing.RefusalText(second.Sent[0].Payload));
        Assert.Equal(MessageType.PairDone, first.Sent.Last().Type);
    }

    [Fact]
    public void ABadNonce_EndsThePairing()
    {
        var exchange = new Exchange().Then(MessageType.PairNonce, new byte[5]);

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairCommit }, exchange.SentTypes);
        Assert.Empty(_display.Shown);
    }

    [Fact]
    public void AListThatCantBeRead_IsNotReplaced()
    {
        File.WriteAllText(_path, "isto não é json");
        var exchange = Confirming();

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(MessageType.PairRefused, exchange.Sent.Last().Type);
        Assert.Contains("Não foi possível gravar", Pairing.RefusalText(exchange.Sent.Last().Payload));
        Assert.Equal("isto não é json", File.ReadAllText(_path));
    }

    [Fact]
    public void TeachersSeenBy_SaysWhoseMachineItIs()
    {
        var service = NewService();
        Assert.Equal(TeacherStatus.None, service.TeachersSeenBy(TeacherA));

        Trust(TeacherA);
        Assert.Equal(TeacherStatus.You, service.TeachersSeenBy(TeacherA));
        Assert.Equal(TeacherStatus.Others, service.TeachersSeenBy(TeacherB));
    }

    [Fact]
    public void TrustTeacher_AddsTheColleague_AndKeepsTheOthers()
    {
        Trust(TeacherA);

        var (succeeded, detail) = NewService().TrustTeacher(TeacherA, new TeacherGrant(TeacherB.ToLowerInvariant(), "PROF-02").Encode());

        Assert.True(succeeded);
        Assert.Equal("liberada", detail);
        Assert.Equal(new[] { TeacherA, TeacherB }, TrustedMastersFile.Load(_path));
        Assert.Empty(_display.Shown);
    }

    [Fact]
    public void TrustTeacher_Twice_KeepsOneEntry()
    {
        Trust(TeacherA);
        var service = NewService();
        var grant = new TeacherGrant(TeacherB, "PROF-02").Encode();

        service.TrustTeacher(TeacherA, grant);
        service.TrustTeacher(TeacherA, grant);

        Assert.Equal(new[] { TeacherA, TeacherB }, TrustedMastersFile.Load(_path));
    }

    public static TheoryData<string> BadGrants => new()
    {
        "{\"thumbprint\":\"\",\"name\":\"PROF-02\"}",
        $"{{\"thumbprint\":\"{new string('B', 39)}\",\"name\":\"PROF-02\"}}",
        $"{{\"thumbprint\":\"G{new string('B', 39)}\",\"name\":\"PROF-02\"}}",
        "{\"name\":\"PROF-02\"}",
        "not json",
    };

    [Theory]
    [MemberData(nameof(BadGrants))]
    public void TrustTeacher_WithoutAThumbprint_IsRefused(string payload)
    {
        Trust(TeacherA);

        var (succeeded, detail) = NewService().TrustTeacher(TeacherA, Encoding.UTF8.GetBytes(payload));

        Assert.False(succeeded);
        Assert.Equal("identificação de professor inválida", detail);
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    [Fact]
    public void TrustTeacher_WithAnUnreadableList_IsRefused_AndLeavesItAlone()
    {
        File.WriteAllText(_path, "not json");

        var (succeeded, detail) = NewService().TrustTeacher(TeacherA, new TeacherGrant(TeacherB, "PROF-02").Encode());

        Assert.False(succeeded);
        Assert.StartsWith("não foi possível gravar o professor nesta máquina", detail);
        Assert.Equal("not json", File.ReadAllText(_path));
    }

    private sealed class FakeDisplay : IPairingCodeDisplay
    {
        public bool SomeoneLoggedOn { get; set; } = true;

        public bool Fails { get; set; }

        public List<(int Code, string Teacher)> Shown { get; } = new();

        public int OpenWindows { get; private set; }

        public IDisposable? Show(int code, string teacherName)
        {
            if (Fails)
            {
                return null;
            }
            Shown.Add((code, teacherName));
            OpenWindows++;
            return new Closer(() => OpenWindows--);
        }

        private sealed class Closer(Action close) : IDisposable
        {
            public void Dispose() => close();
        }
    }

    // The teacher's side, scripted: what it answers, in order, and what the
    // Agent sent it. Out of answers means the teacher hung up.
    private sealed class Exchange
    {
        private readonly Queue<Func<Message>> _answers = new();

        public List<Message> Sent { get; } = new();

        public IEnumerable<MessageType> SentTypes => Sent.Select(message => message.Type);

        public Exchange Then(Func<Message> answer)
        {
            _answers.Enqueue(answer);
            return this;
        }

        public Exchange Then(MessageType type, byte[]? payload = null) =>
            Then(() => new Message(type, payload ?? Array.Empty<byte>()));

        public Message Receive() => _answers.Count > 0 ? _answers.Dequeue()() : throw new IOException("the teacher hung up");

        public void Send(Message message) => Sent.Add(message);
    }
}
