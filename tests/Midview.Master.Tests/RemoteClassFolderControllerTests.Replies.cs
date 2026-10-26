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
        await remote.Loop.WaitAsync(Wait); // the loop ended: nothing asks the home any more
        Assert.True(scripted.Reads > 0);
    }

    // A PC at the home's address with another certificate (Midview reinstalled
    // there, say): told nothing, never taken for closed, and said.
    [Fact]
    public async Task APcWithAnotherIdentity_AtTheHomesAddress_IsToldNothing_AndSaid()
    {
        var certificates = Certificates.Shared;
        var scripted = new ScriptedHome();
        using var host = Open(certificates.Colleague, scripted, (_, _) => Task.FromResult(true)); // not the home's certificate
        using var remote = certificates.Remote(host);

        for (var poll = 0; poll < RemoteClassFolderController.ClosedAfter; poll++)
        {
            await remote.PollAsync();
        }

        var otherIdentity = ClassFolderText.HomeOtherIdentity("PROF-1");
        Assert.Equal(otherIdentity, remote.Note);
        Assert.False(remote.HomeClosed);
        Assert.Equal((false, otherIdentity, string.Empty), await remote.CreateAsync("9º A"));
        Assert.Equal(0, scripted.Reads);
        Assert.Equal(0, scripted.MostCommandsAtOnce);
    }

    // The window's reads between two polls take the last state: they add no requests.
    [Fact]
    public async Task TheWindowsReads_BetweenTwoPolls_TakeTheLastState()
    {
        var certificates = Certificates.Shared;
        var scripted = new ScriptedHome();
        using var host = Open(certificates.Home, scripted, Trusting(certificates.Colleague));
        using var remote = certificates.Remote(host, RemoteClassFolderController.DefaultPollInterval);
        await remote.PollAsync();

        for (var read = 0; read < 5; read++)
        {
            Assert.Equal(OnNow, (await remote.ReadAsync(lessonFiles: true)).Lesson.ToLesson());
        }

        Assert.Equal(1, scripted.Reads);
    }

    // Disposed, it asks nothing more, a poll still asked for is a no-op, and a command is refused.
    [Fact]
    public async Task Disposed_ItAsksNoMore()
    {
        var certificates = Certificates.Shared;
        var scripted = new ScriptedHome();
        using var host = Open(certificates.Home, scripted, Trusting(certificates.Colleague));
        var remote = certificates.Remote(host, TimeSpan.FromMilliseconds(20));
        remote.Start();
        await Until(() => Task.FromResult(scripted.Reads > 0));

        remote.Dispose();
        remote.Dispose();
        await remote.Loop.WaitAsync(Wait); // the loop ended
        var reads = scripted.Reads;
        await remote.PollAsync();

        Assert.Equal(reads, scripted.Reads); // the poll asked nothing
        await Assert.ThrowsAsync<ObjectDisposedException>(() => remote.CreateAsync("9º A"));
    }

    // Task 7a, item 13: what the home says of its own disk ("neste
    // computador"), relayed, is said of the home on this PC ("no PC PROF-1"):
    // in a PC's line, and in a start's.
    [Fact]
    public async Task WhatTheHomeSaysOfItsOwnDisk_IsSaidOfTheHome_OnTheRemote()
    {
        var certificates = Certificates.Shared;
        var unreadable = ClassFolderText.ClassFolderUnreadable("acesso negado");
        // A file that couldn't come in for the home's full disk, as a round says it (Ruling 23).
        var full = "a.docx (não há espaço livre suficiente neste computador) e mais 2";
        var scripted = new ScriptedHome
        {
            Read = () => ScriptedHome.StateOf(OnNow) with
            {
                Pcs = new[]
                {
                    new PcLineDto("PC1", nameof(PcSyncState.Failed), unreadable),
                    new PcLineDto("PC2", nameof(PcSyncState.Failed), full),
                    new PcLineDto("PC3", nameof(PcSyncState.Failed), "neste computador (não há espaço livre suficiente neste computador)"),
                },
            },
            Start = _ => new[] { new ResultLineDto("PC1", false, ClassFolderText.HomeFileUnreadable("acesso negado")) },
        };
        using var host = Open(certificates.Home, scripted, Trusting(certificates.Colleague));
        using var remote = certificates.Remote(host);

        await remote.PollAsync();
        var started = await remote.StartAsync(Turma);

        Assert.Equal(
            new[]
            {
                "a pasta da turma no PC PROF-1 não pôde ser lida: acesso negado",
                "a.docx (não há espaço livre suficiente no PC PROF-1) e mais 2",
                "neste computador (não há espaço livre suficiente no PC PROF-1)",
            },
            remote.Pcs.Select(pc => pc.Detail));
        Assert.Contains($"({FileCollector.NoSpaceText})", full);
        Assert.Equal("não foi possível ler, no PC PROF-1, qual PC guarda as pastas das turmas: acesso negado", started.Single().Detail);
        // On the home itself, as it was.
        Assert.Equal("a pasta da turma neste computador não pôde ser lida: acesso negado", unreadable);
        Assert.Equal(
            "não foi possível ler, neste computador, qual PC guarda as pastas das turmas: acesso negado",
            ClassFolderText.HomeFileUnreadable("acesso negado"));
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

        // What a start answers (none: a start isn't expected).
        public Func<string, IReadOnlyList<ResultLineDto>>? Start { get; init; }

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

        public Task<IReadOnlyList<ResultLineDto>> StartAsync(string turma) =>
            Task.FromResult(Start?.Invoke(turma) ?? throw new NotSupportedException());

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
