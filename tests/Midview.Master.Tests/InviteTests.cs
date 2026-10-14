using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// Both teachers' sides over loopback, with the machines faked (giveClass).
public class InviteTests : IDisposable
{
    private static readonly TimeSpan Listen = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private static readonly InviteResult TheClass = new(
        new[] { new InvitedMachine("ALUNO-01", "10.0.0.1", new string('5', 40), new string('A', 40), "00-1A-2B-3C-4D-5E") },
        new[] { new LeftOutMachine("ALUNO-07", "Offline") });

    private readonly X509Certificate2 _teacherA = SelfSignedCertificate.Create("teacher-a");
    private readonly X509Certificate2 _teacherB = SelfSignedCertificate.Create("teacher-b");
    private readonly List<InviteHost> _hosts = new();
    private readonly List<TeacherGrant> _grants = new();

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }
        _teacherA.Dispose();
        _teacherB.Dispose();
    }

    private InviteHost Open(
        X509Certificate2? certificate = null, string name = "PROF-01", TimeSpan? lifetime = null,
        Func<TeacherGrant, InviteResult>? giveClass = null, Action<string>? joining = null)
    {
        var host = InviteHost.Open(certificate ?? _teacherA, name, giveClass ?? Record, lifetime ?? Long, joining, tcpPort: 0, udpPort: 0);
        _hosts.Add(host);
        return host;
    }

    private InviteResult Record(TeacherGrant grant)
    {
        lock (_grants)
        {
            _grants.Add(grant);
        }
        return TheClass;
    }

    private static IReadOnlyList<IPEndPoint> Targets(params InviteHost[] hosts) =>
        hosts.Select(host => new IPEndPoint(IPAddress.Loopback, host.UdpPort)).ToList();

    private Task<(string From, InviteResult Result)> Join(string code, X509Certificate2 certificate, params InviteHost[] hosts) =>
        Task.Run(() => InviteJoin.Join(code, certificate, "PROF-02", Targets(hosts), Listen)).WaitAsync(Wait);

    [Fact]
    public async Task TheRightCode_GivesTheColleagueTheClass()
    {
        var host = Open();
        var progress = new List<string>();

        var (from, result) = await Task.Run(() => InviteJoin.Join(host.Code, _teacherB, "PROF-02", Targets(host), Listen, progress.Add))
            .WaitAsync(Wait);

        Assert.Equal("PROF-01", from);
        Assert.Equal(TheClass.Given, result.Given);
        Assert.Equal(TheClass.LeftOut, result.LeftOut);
        Assert.Equal(new TeacherGrant(_teacherB.Thumbprint, "PROF-02"), _grants.Single());
        var outcome = await host.Outcome.WaitAsync(Wait);
        Assert.Equal(InviteEnd.Given, outcome.End);
        Assert.Equal("PROF-02", outcome.TeacherName);
        Assert.True(outcome.ListSent);
        Assert.Equal(
            new[] { "Procurando o professor na rede...", "Conectando a PROF-01...", "PROF-01 está liberando as máquinas..." },
            progress);
    }

    [Fact]
    public async Task AWrongSecret_IsRefused_AndEndsTheInvite()
    {
        var host = Open();
        var secret = TeacherInvite.Secret(host.Code) == "ZZZZ" ? "YYYY" : "ZZZZ";
        var wrong = secret + TeacherInvite.Check(_teacherA.Thumbprint, secret);

        var refused = await Assert.ThrowsAsync<InviteJoinException>(() => Join(wrong, _teacherB, host));

        Assert.Equal(InviteHost.WrongCodeText, refused.Message);
        Assert.Equal(InviteEnd.WrongCode, (await host.Outcome.WaitAsync(Wait)).End);
        Assert.Empty(_grants);
        var afterwards = await Assert.ThrowsAsync<InviteJoinException>(() => Join(host.Code, _teacherB, host));
        Assert.Equal(InviteJoin.NotFound, afterwards.Message);
    }

    [Fact]
    public async Task ACodeForAnotherCertificate_NeverSendsTheSecret()
    {
        using var someoneElse = SelfSignedCertificate.Create("someone-else");
        var host = Open();
        var secret = TeacherInvite.Secret(host.Code);
        var elsewhere = secret + TeacherInvite.Check(someoneElse.Thumbprint, secret);

        var refused = await Assert.ThrowsAsync<InviteJoinException>(() => Join(elsewhere, _teacherB, host));

        // Had the secret (the right one) been sent, the class would be given.
        Assert.Equal(InviteJoin.NotFound, refused.Message);
        Assert.False(host.Outcome.IsCompleted);
        Assert.Empty(_grants);
    }

    [Fact]
    public async Task TheInviteExpires()
    {
        var host = Open(lifetime: TimeSpan.FromMilliseconds(300));

        Assert.Equal(InviteEnd.Expired, (await host.Outcome.WaitAsync(Wait)).End);
        var refused = await Assert.ThrowsAsync<InviteJoinException>(() => Join(host.Code, _teacherB, host));
        Assert.Equal(InviteJoin.NotFound, refused.Message);
    }

    [Fact]
    public async Task ClosingTheWindow_CancelsTheInvite_AndStopsAnswering()
    {
        var host = Open();
        Assert.Single(InviteJoin.Find(Targets(host), Listen));

        host.Dispose();

        Assert.Equal(InviteEnd.Cancelled, (await host.Outcome.WaitAsync(Wait)).End);
        Assert.Empty(InviteJoin.Find(Targets(host), Listen));
    }

    [Fact]
    public async Task TheSameIdentity_IsRefused_AndTheInviteStaysOpen()
    {
        var host = Open();

        var refused = await Assert.ThrowsAsync<InviteJoinException>(() => Join(host.Code, _teacherA, host));

        Assert.Equal(InviteHost.SameIdentityText("PROF-01"), refused.Message);
        Assert.False(host.Outcome.IsCompleted);
        var (from, _) = await Join(host.Code, _teacherB, host);
        Assert.Equal("PROF-01", from);
    }

    [Fact]
    public async Task OnlyOneColleagueAtATime()
    {
        using var release = new ManualResetEventSlim();
        using var joined = new ManualResetEventSlim();
        var host = Open(
            giveClass: grant =>
            {
                release.Wait(Wait);
                return Record(grant);
            },
            joining: _ => joined.Set());

        var first = Join(host.Code, _teacherB, host);
        Assert.True(joined.Wait(Wait));

        // A second connection meanwhile is closed unanswered.
        using var tcp = new TcpClient();
        tcp.Connect(IPAddress.Loopback, host.TcpPort);
        tcp.ReceiveTimeout = 10_000;
        Assert.ThrowsAny<Exception>(() =>
        {
            using var channel = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, "localhost", _teacherB);
            channel.Receive();
        });

        release.Set();
        var (from, _) = await first;
        Assert.Equal("PROF-01", from);
    }

    [Fact]
    public async Task ClosingTheWindow_WhileTheClassIsBeingGiven_StillEndsAsGiven()
    {
        using var release = new ManualResetEventSlim();
        using var joined = new ManualResetEventSlim();
        var host = Open(
            giveClass: grant =>
            {
                release.Wait(Wait);
                return Record(grant);
            },
            joining: _ => joined.Set());

        var join = Join(host.Code, _teacherB, host);
        Assert.True(joined.Wait(Wait));

        host.Dispose();
        Assert.False(host.Outcome.IsCompleted);

        release.Set();
        var (from, result) = await join;
        var outcome = await host.Outcome.WaitAsync(Wait);
        Assert.Equal("PROF-01", from);
        Assert.Equal(TheClass.Given, result.Given);
        Assert.Equal(InviteEnd.Given, outcome.End);
        Assert.True(outcome.ListSent);
    }

    [Fact]
    public async Task AFailingJoiningCallback_DoesNotStopTheClassBeingGiven()
    {
        var host = Open(joining: _ => throw new IOException("window gone"));

        var (from, result) = await Join(host.Code, _teacherB, host);

        Assert.Equal("PROF-01", from);
        Assert.Equal(TheClass.Given, result.Given);
        Assert.Equal(new TeacherGrant(_teacherB.Thumbprint, "PROF-02"), _grants.Single());
        var outcome = await host.Outcome.WaitAsync(Wait);
        Assert.Equal(InviteEnd.Given, outcome.End);
        Assert.Equal("PROF-02", outcome.TeacherName);
        Assert.True(outcome.ListSent);
    }

    [Fact]
    public async Task WithTwoInvitesOpen_TheCodeFindsItsOwnTeacher()
    {
        using var teacherC = SelfSignedCertificate.Create("teacher-c");
        var other = Open(teacherC, "PROF-03");
        var host = Open();

        var (from, _) = await Join(host.Code, _teacherB, other, host);

        Assert.Equal("PROF-01", from);
        Assert.Equal(InviteEnd.Given, (await host.Outcome.WaitAsync(Wait)).End);
        Assert.False(other.Outcome.IsCompleted);
    }

    [Fact]
    public void APortInUse_CannotOpenASecondInvite()
    {
        var host = Open();

        Assert.Throws<SocketException>(() => InviteHost.Open(_teacherA, "PROF-01", Record, Long, tcpPort: host.TcpPort, udpPort: 0));
    }

    [Fact]
    public void TheMachinesThatCame_GoIntoTheClassroomListAsTheyAre()
    {
        var result = new InviteResult(
            new[] { new InvitedMachine("ALUNO-01", "PC-ALUNO-01", new string('e', 40), new string('a', 40), "00:1a:2b:3c:4d:5e") },
            Array.Empty<LeftOutMachine>());

        Assert.Equal(
            new KnownMachine("ALUNO-01", "PC-ALUNO-01", new string('E', 40), new string('A', 40), "00-1A-2B-3C-4D-5E"),
            InviteJoin.ToKnownMachines(result).Single());
    }
}
