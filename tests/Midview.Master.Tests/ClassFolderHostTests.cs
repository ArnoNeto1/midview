using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHost: the home PC's Master serving its "Pasta da turma" window to
// a colleague's PC (plan decisions 5 and 6), on loopback only, on a port the
// system picks. The home is the rig's Master (PROF-1) with a real
// LocalClassFolderController over ClassSyncRig's PCs; the colleague is the
// rig's OtherMaster. A fake controller checks what the host itself must do with
// whatever a controller gives it. The connections themselves (misbehaving
// clients, the deadline, the caps, closing the host) are in
// ClassFolderHostTests.Connections.cs.
public sealed partial class ClassFolderHostTests
{
    private const string Turma = ClassSyncRig.Turma;

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    private static DateTime Utc(int minutes) => new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);

    [Fact]
    public async Task ATrustedColleague_ReadsTheState_AndStartsAndEndsTheLesson()
    {
        using var rig = new ClassSyncRig();
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        using var host = Open(rig.Master, controller, Trusting(rig.OtherMaster));

        var before = State(host, rig.OtherMaster, rig.Master);
        Assert.Equal(ClassSyncRig.MachineName, before.HomeName);
        Assert.False(before.Lesson.ToLesson().IsOn);
        Assert.Equal(new[] { Turma, "3º B" }, before.Classes.Select(summary => summary.Turma));
        Assert.Equal(new[] { "PC1", "PC2", "PC3" }, before.Pcs.Select(pc => pc.Name));

        var started = await Task.Run(() => Command(host, rig.OtherMaster, rig.Master, new RemoteCommand(RemoteCommand.Start, Turma, null, null)));

        Assert.True(started.Ok, started.Detail);
        Assert.Equal(Turma, started.Turma);
        Assert.Equal(new[] { "PC1", "PC2", "PC3" }, started.Lines.Select(line => line.Name));
        Assert.All(started.Lines, line => Assert.True(line.Ok, $"{line.Name}: {line.Detail}"));
        Assert.True(controller.Lesson.IsOn);
        Assert.Equal(Turma, controller.Lesson.Turma);

        var during = State(host, rig.OtherMaster, rig.Master);
        Assert.Equal(controller.Lesson, during.Lesson.ToLesson());
        Assert.Equal(Turma, during.LessonFiles?.Turma);

        var ended = await Task.Run(() => Command(
            host, rig.OtherMaster, rig.Master, new RemoteCommand(RemoteCommand.End, Turma, null, RemoteCommand.Keep)));

        Assert.True(ended.Ok, ended.Detail);
        Assert.Equal(3, ended.Lines.Count);
        Assert.All(ended.Lines, line => Assert.True(line.Ok, $"{line.Name}: {line.Detail}"));
        Assert.False(controller.Lesson.IsOn);
    }

    [Fact]
    public async Task ATrustedColleague_CreatesRenamesAndDeletesClasses()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        using var host = Open(rig.Master, controller, Trusting(rig.OtherMaster));
        RemoteReply Send(RemoteCommand command) => Command(host, rig.OtherMaster, rig.Master, command);

        var created = await Task.Run(() => Send(new RemoteCommand(RemoteCommand.Create, "Turma 1/2", null, null)));
        Assert.Equal((true, string.Empty, "Turma 1-2"), (created.Ok, created.Detail, created.Turma));
        Assert.True(rig.Store.Exists("Turma 1-2"));

        var taken = await Task.Run(() => Send(new RemoteCommand(RemoteCommand.Create, "Turma 1-2", null, null)));
        Assert.False(taken.Ok);
        Assert.NotEmpty(taken.Detail);

        var renamed = await Task.Run(() => Send(new RemoteCommand(RemoteCommand.Rename, "Turma 1-2", "9º A", null)));
        Assert.Equal((true, "9º A"), (renamed.Ok, renamed.Turma));
        Assert.True(rig.Store.Exists("9º A"));

        var deleted = await Task.Run(() => Send(new RemoteCommand(RemoteCommand.Delete, "9º A", null, null)));
        Assert.True(deleted.Ok, deleted.Detail);
        Assert.False(rig.Store.Exists("9º A"));

        // The home's guards answer the colleague too, in the teacher's words.
        await controller.StartAsync(Turma);
        var lessonClass = await Task.Run(() => Send(new RemoteCommand(RemoteCommand.Delete, Turma, null, null)));
        Assert.Equal((false, ClassFolderText.LessonClassNotDeleted), (lessonClass.Ok, lessonClass.Detail));
        var second = await Task.Run(() => Send(new RemoteCommand(RemoteCommand.Start, "3º B", null, null)));
        Assert.Equal((false, ClassFolderText.LessonAlreadyOn(Turma)), (second.Ok, second.Detail));
        Assert.Empty(second.Lines);
        Assert.Equal(Turma, controller.Lesson.Turma);
    }

    [Fact]
    public async Task AStranger_IsRefused_AndChangesNothing()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        using var stranger = SelfSignedCertificate.Create("stranger-under-test");
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        var asked = new ConcurrentQueue<string>();
        using var host = Open(rig.Master, controller, thumbprint =>
        {
            asked.Enqueue(thumbprint);
            return Task.FromResult(false);
        });

        var state = Ask(host, stranger, rig.Master, new Message(MessageType.RemoteClassFolderState, Array.Empty<byte>()));
        var start = await Task.Run(() => Ask(host, stranger, rig.Master, new Message(
            MessageType.RemoteClassFolderCommand, ClassFolderRemote.EncodeJson(new RemoteCommand(RemoteCommand.Start, Turma, null, null)))));

        foreach (var answer in new[] { state, start })
        {
            Assert.Equal(MessageType.CommandAck, answer.Type);
            Assert.Equal((false, ClassFolderRemote.NotTrustedText), CommandResult.Decode(answer.Payload));
        }
        Assert.False(controller.Lesson.IsOn);
        Assert.Equal(new[] { stranger.Thumbprint, stranger.Thumbprint }, asked.Select(thumbprint => thumbprint.ToUpperInvariant()));
    }

    // Review Focus 1: two teacher PCs can share one teacher identity; the
    // other one is a colleague without asking anybody.
    [Fact]
    public void APcWithTheHomesOwnIdentity_IsAdmitted_WithoutAsking()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        var asked = 0;
        using var host = Open(rig.Master, controller, _ =>
        {
            Interlocked.Increment(ref asked);
            return Task.FromResult(false);
        });

        var state = State(host, rig.Master, rig.Master);

        Assert.Equal(new[] { Turma, "3º B" }, state.Classes.Select(summary => summary.Turma));
        Assert.Equal(0, Volatile.Read(ref asked));
    }

    // Review Focus 3: the home teacher starts the next class while the
    // colleague's end is still bringing the last files in. They run one after
    // the other; both say what they did, and both windows read the same lesson.
    [Fact]
    public async Task TheHomesClickAndAColleaguesCommand_RunOneAfterTheOther_AndBothReport()
    {
        using var rig = new ClassSyncRig();
        var sync = rig.NewSync();
        var controller = LocalClassFolderControllerTests.NewController(rig, sync);
        using var host = Open(rig.Master, controller, Trusting(rig.OtherMaster));
        await controller.StartAsync(Turma);
        rig.Pcs[0].Write("ultimo.txt", new byte[] { 1, 2, 3 }, Utc(2));
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        sync.CopyingStarts = _ =>
        {
            reached.Set();
            release.Wait(Wait);
        };

        var colleaguesEnd = Task.Run(() => Command(
            host, rig.OtherMaster, rig.Master, new RemoteCommand(RemoteCommand.End, Turma, null, RemoteCommand.Keep)));
        Assert.True(reached.Wait(Wait));
        var homesStart = Task.Run(() => controller.StartAsync("3º B"));
        await Task.WhenAny(homesStart, Task.Delay(500));
        Assert.False(homesStart.IsCompleted);
        Assert.Equal(Turma, controller.Lesson.Turma);

        release.Set();
        var ended = await colleaguesEnd.WaitAsync(Wait);
        var started = await homesStart.WaitAsync(Wait);

        Assert.True(ended.Ok, ended.Detail);
        Assert.All(ended.Lines, line => Assert.True(line.Ok, $"{line.Name}: {line.Detail}"));
        Assert.Equal(3, started.Count);
        Assert.All(started, line => Assert.True(line.Ok, $"{line.Name}: {line.Detail}"));
        Assert.Equal(new[] { "ultimo.txt" }, rig.StoreFiles());
        Assert.True(controller.Lesson.IsOn);
        Assert.Equal("3º B", controller.Lesson.Turma);
        Assert.Equal(controller.Lesson, State(host, rig.OtherMaster, rig.Master).Lesson.ToLesson());
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"kind\":\"format\",\"turma\":\"3º A\"}")]
    [InlineData("{\"kind\":\"start\"}")]
    [InlineData("{\"kind\":\"end\",\"turma\":\"3º A\"}")]
    [InlineData("{\"kind\":\"rename\",\"turma\":\"3º A\"}")]
    public void AMalformedOrIncompleteCommand_GetsAnErrorReply(string json)
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        using var host = Open(rig.Master, controller, Trusting(rig.OtherMaster));

        var answer = Ask(host, rig.OtherMaster, rig.Master, new Message(MessageType.RemoteClassFolderCommand, Encoding.UTF8.GetBytes(json)));

        Assert.Equal(MessageType.RemoteClassFolderCommandReply, answer.Type);
        var reply = ClassFolderRemote.DecodeJson<RemoteReply>(answer.Payload);
        Assert.NotNull(reply);
        Assert.Equal((false, ClassFolderHost.IncompleteText), (reply!.Ok, reply.Detail));
        Assert.False(controller.Lesson.IsOn);
        Assert.True(rig.Store.Exists(Turma));
    }

    [Fact]
    public void AnyOtherMessage_IsRefused()
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        using var host = Open(rig.Master, controller, Trusting(rig.OtherMaster));

        var answer = Ask(host, rig.OtherMaster, rig.Master, new Message(MessageType.SetClassLesson, Array.Empty<byte>()));

        Assert.Equal(MessageType.CommandAck, answer.Type);
        Assert.Equal((false, ClassFolderRemote.UnknownRequestText), CommandResult.Decode(answer.Payload));
    }

    // Whatever a colleague's PC sends as a class name, only a class name
    // reaches the store; anything else is a class that isn't there.
    [Theory]
    [InlineData(RemoteCommand.Rename)]
    [InlineData(RemoteCommand.Delete)]
    [InlineData(RemoteCommand.Start)]
    [InlineData(RemoteCommand.End)]
    public async Task ANameThatIsNoClass_IsAClassThatIsGone(string kind)
    {
        using var rig = new ClassSyncRig(pcs: 1);
        var controller = LocalClassFolderControllerTests.NewController(rig, rig.NewSync());
        using var host = Open(rig.Master, controller, Trusting(rig.OtherMaster));
        var outside = Path.Combine(rig.Root, "fora");
        Directory.CreateDirectory(outside);

        var reply = await Task.Run(() => Command(
            host, rig.OtherMaster, rig.Master, new RemoteCommand(kind, @"..\fora", "9º A", RemoteCommand.All)));

        Assert.Equal((false, ClassFolderText.ClassGone), (reply.Ok, reply.Detail));
        Assert.True(Directory.Exists(outside));
        Assert.False(rig.Store.Exists("9º A"));
        Assert.False(controller.Lesson.IsOn);
    }

    // The colleague's PC shows what the home says as it is: Portuguese, never
    // an exception's own message.
    [Fact]
    public void AFailureOnTheHome_IsToldInTheTeachersWords()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        using var colleague = SelfSignedCertificate.Create("colleague-under-test");
        var controller = new FakeController
        {
            Read = _ => throw new InvalidOperationException("internal state broke"),
            Delete = _ => throw new InvalidOperationException("internal state broke"),
            Start = _ => Task.FromException<IReadOnlyList<ResultLineDto>>(
                new UnauthorizedAccessException("Access to the path is denied.")),
        };
        using var host = Open(master, controller, Trusting(colleague));

        var state = Ask(host, colleague, master, new Message(MessageType.RemoteClassFolderState, Array.Empty<byte>()));
        var deleted = Command(host, colleague, master, new RemoteCommand(RemoteCommand.Delete, Turma, null, null));
        var started = Command(host, colleague, master, new RemoteCommand(RemoteCommand.Start, Turma, null, null));

        Assert.Equal(MessageType.CommandAck, state.Type);
        Assert.Equal((false, ClassFolderRemote.UnreadableText + ClassFolderHost.UnexpectedReason), CommandResult.Decode(state.Payload));
        Assert.Equal((false, ClassFolderText.DeleteFailed(ClassFolderHost.UnexpectedReason)), (deleted.Ok, deleted.Detail));
        Assert.Equal((false, ClassFolderText.StartFailed(Turma, "acesso negado")), (started.Ok, started.Detail));
    }

    // Ruling 4: a class folder made by hand with a name over 60 characters is
    // left out of the state, and a PC's name over 64 is cut, instead of the
    // colleague's PC refusing the whole state (or reply).
    [Fact]
    public void WhatTheMessagesCantCarry_IsLeftOutOrCut()
    {
        using var master = SelfSignedCertificate.Create("master-under-test");
        using var colleague = SelfSignedCertificate.Create("colleague-under-test");
        var longPc = new string('P', 70);
        var controller = new FakeController
        {
            Read = _ => new RemoteState(
                "PROF-1", ClassLessonDto.From(ClassLesson.None(0)),
                new[] { new ClassSummaryDto("3º A", 1, 10, null), new ClassSummaryDto(new string('t', 61), 1, 10, null) },
                null, new[] { new PcLineDto(longPc, nameof(PcSyncState.Offline), string.Empty) }, Array.Empty<string>(), 100, 50),
            Start = _ => Task.FromResult<IReadOnlyList<ResultLineDto>>(new[] { new ResultLineDto(longPc, true, string.Empty) }),
        };
        using var host = Open(master, controller, Trusting(colleague));

        var state = State(host, colleague, master);
        var started = Command(host, colleague, master, new RemoteCommand(RemoteCommand.Start, Turma, null, null));

        Assert.Equal(new[] { "3º A" }, state.Classes.Select(summary => summary.Turma));
        Assert.Equal(longPc[..ClassHome.MaxNameLength], Assert.Single(state.Pcs).Name);
        Assert.Equal(longPc[..ClassHome.MaxNameLength], Assert.Single(started.Lines).Name);
    }

    private static ClassFolderHost Open(X509Certificate2 certificate, IClassFolderController controller, Func<string, Task<bool>> isTrusted) =>
        ClassFolderHost.Open(certificate, controller, isTrusted, 0, _ => { }, IPAddress.Loopback)
        ?? throw new InvalidOperationException("The host could not listen.");

    private static Func<string, Task<bool>> Trusting(X509Certificate2 colleague) =>
        thumbprint => Task.FromResult(string.Equals(thumbprint, colleague.Thumbprint, StringComparison.OrdinalIgnoreCase));

    // One request on its own connection, as the colleague's PC sends it, and the answer.
    private static Message Ask(ClassFolderHost host, X509Certificate2 client, X509Certificate2 home, Message request)
    {
        using var tcp = new TcpClient();
        tcp.Connect(IPAddress.Loopback, host.Port);
        tcp.ReceiveTimeout = (int)Wait.TotalMilliseconds;
        tcp.SendTimeout = (int)Wait.TotalMilliseconds;
        using var channel = SecureChannel.AuthenticateAsClient(tcp, "localhost", client, new TrustStore(new[] { home.Thumbprint }));
        channel.Send(request);
        return channel.Receive();
    }

    private static RemoteState State(ClassFolderHost host, X509Certificate2 client, X509Certificate2 home)
    {
        var answer = Ask(host, client, home, new Message(MessageType.RemoteClassFolderState, Array.Empty<byte>()));
        Assert.Equal(MessageType.RemoteClassFolderStateReply, answer.Type);
        return ClassFolderRemote.DecodeJson<RemoteState>(answer.Payload) ?? throw new InvalidDataException("Not a state.");
    }

    // The state, asked again until the host takes the connection (it may still hold this PC's last few).
    private static async Task<RemoteState> StateSoon(ClassFolderHost host, X509Certificate2 client, X509Certificate2 home)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return await Task.Run(() => State(host, client, home));
            }
            catch (Exception) when (clock.Elapsed < Wait)
            {
                await Task.Delay(100);
            }
        }
    }

    private static RemoteState EmptyState() => new(
        "PROF-1", ClassLessonDto.From(ClassLesson.None(0)), Array.Empty<ClassSummaryDto>(), null, Array.Empty<PcLineDto>(),
        Array.Empty<string>(), 0, 0);

    private static RemoteReply Command(ClassFolderHost host, X509Certificate2 client, X509Certificate2 home, RemoteCommand command)
    {
        var answer = Ask(host, client, home, new Message(MessageType.RemoteClassFolderCommand, ClassFolderRemote.EncodeJson(command)));
        Assert.Equal(MessageType.RemoteClassFolderCommandReply, answer.Type);
        return ClassFolderRemote.DecodeJson<RemoteReply>(answer.Payload) ?? throw new InvalidDataException("Not a reply.");
    }

    // A controller that answers what the test says, for what the host does on its own.
    private sealed class FakeController : IClassFolderController
    {
        public Func<bool, RemoteState>? Read { get; init; }

        public Func<string, Task<IReadOnlyList<ResultLineDto>>>? Start { get; init; }

        public Func<string, Task<(bool Ok, string Detail)>>? Delete { get; init; }

        public ClassLesson Lesson => ClassLesson.None(0);

        public IReadOnlyList<PcLineDto> Pcs => Array.Empty<PcLineDto>();

        public IReadOnlyList<string> LastEndProblems => Array.Empty<string>();

        public string? HomeName => null;

        public bool HomeClosed => false;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Task<RemoteState> ReadAsync(bool lessonFiles) => Task.FromResult(Read!(lessonFiles));

        public Task<(bool Ok, string Detail, string Turma)> CreateAsync(string name) => throw new NotSupportedException();

        public Task<(bool Ok, string Detail)> RenameAsync(string turma, string newName) => throw new NotSupportedException();

        public Task<(bool Ok, string Detail)> DeleteAsync(string turma) => Delete!(turma);

        public Task<IReadOnlyList<ResultLineDto>> StartAsync(string turma) => Start!(turma);

        public Task<IReadOnlyList<ResultLineDto>> EndAsync(string turma, EndChoice choice) => throw new NotSupportedException();
    }
}
