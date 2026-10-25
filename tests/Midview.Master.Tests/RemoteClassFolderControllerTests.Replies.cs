using System.Net;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// RemoteClassFolderController against a scripted home controller behind a
// real ClassFolderHost: what the remote does with each kind of answer, and
// that it never has more than one state read and one command at the home at once.
public sealed partial class RemoteClassFolderControllerTests
{
    private static readonly ClassLesson OnNow = new(100, Guid.NewGuid(), Turma, 50, Ended: false);

    // The home can't read its class folders just now (UnreadableText): the
    // last view stays, with the home's note, and comes back with the next state.
    [Fact]
    public async Task AHomeThatCantReadItsFolders_KeepsTheLastView_WithItsNote()
    {
        var home = Certificates.Shared;
        var scripted = new ScriptedHome();
        using var host = Open(home.Home, scripted, Trusting(home.Colleague));
        using var remote = home.Remote(host);
        await remote.PollAsync();
        Assert.Equal(OnNow, remote.Lesson);
        var changes = 0;
        remote.Changed += () => Interlocked.Increment(ref changes);

        scripted.Read = () => throw new IOException("the disk went away");
        await remote.PollAsync();

        Assert.Equal(ClassFolderRemote.UnreadableText + ClassFolderHost.UnexpectedReason, remote.Note);
        Assert.Equal(OnNow, remote.Lesson);
        Assert.Equal(ScriptedHome.Pcs1, remote.Pcs);
        Assert.False(remote.HomeClosed);
        Assert.Equal(1, changes);
        await Assert.ThrowsAnyAsync<IOException>(() => remote.ReadAsync(lessonFiles: true)); // the window keeps what it shows

        scripted.Read = () => ScriptedHome.StateOf(OnNow);
        await remote.PollAsync();

        Assert.Null(remote.Note);
        Assert.Equal(2, changes);
    }

    // The home doesn't take this PC's requests (NotTrustedText): nothing of
    // the home is shown any more, the note says why, and commands are refused.
    [Fact]
    public async Task AHomeThatDoesNotTakeThisPc_ShowsNothingOfIt_AndSaysWhy()
    {
        var home = Certificates.Shared;
        var trusted = true;
        using var host = Open(home.Home, new ScriptedHome(), (_, _) => Task.FromResult(Volatile.Read(ref trusted)));
        using var remote = home.Remote(host);
        await remote.PollAsync();
        Assert.Equal(OnNow, remote.Lesson);

        Volatile.Write(ref trusted, false);
        await remote.PollAsync();

        Assert.Equal(ClassFolderRemote.NotTrustedText, remote.Note);
        Assert.False(remote.Lesson.IsOn);
        Assert.Empty(remote.Pcs);
        Assert.False(remote.HomeClosed);
        Assert.Equal((false, ClassFolderRemote.NotTrustedText, string.Empty), await remote.CreateAsync("9º A"));
        Assert.Equal(
            ClassFolderRemote.NotTrustedText,
            (await Assert.ThrowsAsync<ClassFolderRefusedException>(() => remote.StartAsync(Turma))).Message);
    }

    // Every 2 s, plus the window's own reads, plus a read after each command:
    // still one state read at the home at a time, and one command; none once stopped.
    [Fact]
    public async Task StateReadsAndCommands_ReachTheHomeOneAtATime_AndStopWhenStopped()
    {
        var home = Certificates.Shared;
        var scripted = new ScriptedHome { Hold = TimeSpan.FromMilliseconds(100) };
        using var host = Open(home.Home, scripted, Trusting(home.Colleague));
        using var remote = home.Remote(host, TimeSpan.FromMilliseconds(10));

        remote.Start();
        var reads = Enumerable.Range(0, 4).Select(_ => Task.Run(() => remote.ReadAsync(lessonFiles: true))).ToList();
        var creates = Enumerable.Range(0, 3).Select(number => Task.Run(() => remote.CreateAsync($"Turma {number}"))).ToList();
        await Task.WhenAll(reads).WaitAsync(Wait);
        var created = await Task.WhenAll(creates).WaitAsync(Wait);
        remote.Stop();

        Assert.All(created, result => Assert.True(result.Ok, result.Detail));
        Assert.Equal(1, scripted.MostReadsAtOnce);
        Assert.Equal(1, scripted.MostCommandsAtOnce);
        await Task.Delay(300);
        var readsSoFar = scripted.Reads;
        await Task.Delay(300);
        Assert.Equal(readsSoFar, scripted.Reads);
    }

    // The home PC's certificate and a colleague's, and the colleague's remote
    // for a host on loopback (no Agents: these homes never close). One pair
    // for these tests (made once: RSA keys are slow to make, and the suite runs
    // timing tests alongside), kept for the test run.
    private sealed class Certificates
    {
        private static readonly Lazy<Certificates> Made = new(() => new Certificates());

        private Certificates()
        {
        }

        public static Certificates Shared => Made.Value;

        public X509Certificate2 Home { get; } = SelfSignedCertificate.Create("master-under-test");

        public X509Certificate2 Colleague { get; } = SelfSignedCertificate.Create("other-master-under-test");

        public RemoteClassFolderController Remote(ClassFolderHost host, TimeSpan? pollInterval = null) => new(
            Colleague, () => new ClassHome(Home.Thumbprint, "PROF-1", IPAddress.Loopback.ToString(), host.Port, 1),
            () => Array.Empty<KnownMachine>(), _ => false, pollInterval ?? TimeSpan.FromHours(1));
    }

    // A home whose state is what the test says (a lesson on, one PC copying),
    // whose reads and creations each take Hold, and which counts how many of
    // each reach it at once.
    private sealed class ScriptedHome : IClassFolderController
    {
        public static readonly IReadOnlyList<PcLineDto> Pcs1 = new[] { new PcLineDto("PC1", nameof(PcSyncState.Copying), string.Empty) };

        private int _reading;
        private int _commanding;
        private int _mostReads;
        private int _mostCommands;
        private int _reads;

        public Func<RemoteState> Read { get; set; } = () => StateOf(OnNow);

        public TimeSpan Hold { get; init; }

        public int MostReadsAtOnce => Volatile.Read(ref _mostReads);

        public int MostCommandsAtOnce => Volatile.Read(ref _mostCommands);

        public int Reads => Volatile.Read(ref _reads);

        public ClassLesson Lesson => OnNow;

        public IReadOnlyList<PcLineDto> Pcs => Pcs1;

        public IReadOnlyList<string> LastEndProblems => Array.Empty<string>();

        public string? HomeName => null;

        public bool HomeClosed => false;

        public string? Note => null;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public static RemoteState StateOf(ClassLesson lesson) => new(
            "PROF-1", ClassLessonDto.From(lesson), Array.Empty<ClassSummaryDto>(), new LessonFilesDto(lesson.Turma, 0, 0, 0, 0),
            Pcs1, Array.Empty<string>(), long.MaxValue, 0);

        public async Task<RemoteState> ReadAsync(bool lessonFiles)
        {
            Interlocked.Increment(ref _reads);
            Raise(ref _mostReads, Interlocked.Increment(ref _reading));
            try
            {
                await Task.Delay(Hold);
                return Read();
            }
            finally
            {
                Interlocked.Decrement(ref _reading);
            }
        }

        public async Task<(bool Ok, string Detail, string Turma)> CreateAsync(string name)
        {
            Raise(ref _mostCommands, Interlocked.Increment(ref _commanding));
            try
            {
                await Task.Delay(Hold);
                return (true, string.Empty, name);
            }
            finally
            {
                Interlocked.Decrement(ref _commanding);
            }
        }

        public Task<(bool Ok, string Detail)> RenameAsync(string turma, string newName) => throw new NotSupportedException();

        public Task<(bool Ok, string Detail)> DeleteAsync(string turma) => throw new NotSupportedException();

        public Task<IReadOnlyList<ResultLineDto>> StartAsync(string turma) => throw new NotSupportedException();

        public Task<IReadOnlyList<ResultLineDto>> EndAsync(string turma, EndChoice choice) => throw new NotSupportedException();

        private static void Raise(ref int most, int now)
        {
            for (var seen = Volatile.Read(ref most); now > seen; seen = Volatile.Read(ref most))
            {
                if (Interlocked.CompareExchange(ref most, now, seen) == seen)
                {
                    return;
                }
            }
        }
    }
}
