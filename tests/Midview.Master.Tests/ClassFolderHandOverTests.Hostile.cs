using System.Net;
using System.Net.Sockets;
using System.Text;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver against a home that sends what it shouldn't (a path out
// of the store, sizes that don't match, a copy that isn't whole: refused, and
// nothing written anywhere), with the home unreachable (the take-over claims
// the Agents and keeps this PC's own classes), and the window's texts.
public sealed partial class ClassFolderHandOverTests
{
    private const string Manifest = "manifest.json";

    private static readonly long Ticks = Earlier.Ticks;

    public static TheoryData<string> HostileScripts => new()
    {
        "path out of the store", "path out of the class", "path the manifest doesn't have", "header size", "more bytes",
        "fewer bytes", "a file missing", "a class not listed", "the same path twice", "a file and a folder", "an index too big",
        "a bad class name", "a bad time", "a manifest that isn't one", "no manifest",
    };

    [Theory]
    [MemberData(nameof(HostileScripts))]
    public async Task AHostileHome_IsRefused_AndNothingIsWrittenAnywhere(string script)
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig, openHost: false);
        await lab.MakeTheOldHomeTheHomeAsync();
        lab.GiveTheNewHomeAClassOfItsOwn();
        var before = lab.Snapshot();
        using var home = new ScriptedHandOverHome(rig, channel => Hostile(channel, script));

        var result = await lab.Taker.TakeOverAsync(lab.Home with { Port = home.Port });

        Assert.False(result.Ok, script);
        Assert.Equal(ClassFolderText.TakeOverFailed(OldHome, ClassFolderText.CopyMismatch(OldHome)), result.Detail);
        lab.AssertUnchanged(before);
        Assert.Empty(Directory.GetFiles(rig.Root, "fora*", SearchOption.AllDirectories));
        Assert.Null(ClassHomeFileMaster.Read(lab.NewHomePath));
    }

    // The whole store must fit on this PC's disk with 1 GB to spare: refused before anything is written.
    [Fact]
    public async Task AStoreThatDoesntFit_IsRefusedBeforeAnythingIsWritten()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig, openHost: false);
        await lab.MakeTheOldHomeTheHomeAsync();
        var before = lab.Snapshot();
        lab.NewFreeSpace = ClassFolderHandOver.KeepFreeBytes + 99;
        var wrote = false;
        using var home = new ScriptedHandOverHome(rig, channel =>
        {
            SendManifest(channel, new[] { Turma }, ($"files/{Turma}/a.txt", 100, Ticks));
            try
            {
                channel.Receive(); // until the taker hangs up
            }
            finally
            {
                wrote = Directory.Exists(Path.Combine(lab.NewRoot, "Turmas (recebendo)"));
            }
        });

        var result = await lab.Taker.TakeOverAsync(lab.Home with { Port = home.Port });

        Assert.False(result.Ok);
        Assert.Contains("1 GB", result.Detail);
        Assert.False(wrote);
        lab.AssertUnchanged(before);
    }

    // A home that sends a well-formed copy is taken (the scripted home is not hostile by itself).
    [Fact]
    public async Task AScriptedHomeSendingAWholeCopy_IsTaken()
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig, openHost: false);
        await lab.MakeTheOldHomeTheHomeAsync();
        using var home = new ScriptedHandOverHome(rig, channel =>
        {
            SendManifest(channel, new[] { Turma, "3º B" }, ($"files/{Turma}/sub/a.txt", 3, Ticks), ("lessons.json", 2, Ticks));
            SendFile(channel, $"files/{Turma}/sub/a.txt", new byte[] { 1, 2, 3 });
            SendFile(channel, "lessons.json", Encoding.UTF8.GetBytes("[]"));
            Done(channel);
        });

        var result = await lab.Taker.TakeOverAsync(lab.Home with { Port = home.Port });

        Assert.True(result.Ok, result.Detail);
        Assert.Equal(new[] { Turma, "3º B" }, lab.NewStore.List().Select(summary => summary.Turma));
        Assert.Equal(Earlier, File.GetLastWriteTimeUtc(lab.NewPath(Turma, "sub/a.txt")));
        Assert.Equal(NewHome, rig.Pcs[0].Agent.CurrentHome!.Name);
        lab.AssertNoStaging();
    }

    private const string LessonOnThere =
        "O PC PROF-1 está com a aula do 3º A em andamento: ele deixa de guardar as pastas quando essa aula acabar, "
        + "e a cópia dele continua na pasta Turmas dele.";

    // What the old home answers RemoteHandOverDone (stood down, detail), and the end of the result line.
    public static TheoryData<bool, string, string> OldHomeAnswers => new()
    {
        { true, "Turmas (passadas para PROF-2) (2)", "A cópia do PC PROF-1 ficou nele, na pasta \"Turmas (passadas para PROF-2) (2)\"." },
        { true, "|acesso negado", "O PC PROF-1 não conseguiu pôr a cópia dele à parte (acesso negado): ela continua na pasta Turmas dele." },
        { true, "", "O PC PROF-1 não tinha turmas para guardar à parte." },
        { false, LessonOnThere, LessonOnThere },
        { false, "", "O PC PROF-1 ainda não confirmou a troca: a cópia dele continua na pasta Turmas dele." },
        // Whatever that PC says goes on one line, of at most 200 characters.
        { false, "uma linha\r\noutra\tlinha\u0007", "uma linha outra linha" },
        { false, new string('x', 300), new string('x', 200) + "…" },
        { true, "Turmas (passadas\npara PROF-2)", "A cópia do PC PROF-1 ficou nele, na pasta \"Turmas (passadas para PROF-2)\"." },
        // The case goes by what that PC said, not by what is left of it to show.
        { true, "|", "O PC PROF-1 não conseguiu pôr a cópia dele à parte (motivo desconhecido): ela continua na pasta Turmas dele." },
        { true, "|\r\n ", "O PC PROF-1 não conseguiu pôr a cópia dele à parte (motivo desconhecido): ela continua na pasta Turmas dele." },
        { true, "   ", "A cópia do PC PROF-1 ficou nele, numa pasta à parte." },
        { false, " \n ", "O PC PROF-1 ainda não confirmou a troca: a cópia dele continua na pasta Turmas dele." },
        // Its copy went aside but not all of it, and couldn't come back.
        { true, "*Turmas (passadas para PROF-2)", "A cópia do PC PROF-1 ficou nele, na pasta \"Turmas (passadas para PROF-2)\", mas nem tudo foi para lá: confira as turmas dele." },
        { true, "* \n", "A cópia do PC PROF-1 ficou nele, numa pasta à parte, mas nem tudo foi para lá: confira as turmas dele." },
    };

    // What the old home answered
    // RemoteHandOverDone is in the result line: the folder its copy went to
    // (numbered, say), why it couldn't go aside, that it had none, or that it
    // keeps the folders until its lesson ends.
    [Theory]
    [MemberData(nameof(OldHomeAnswers))]
    public async Task WhatTheOldHomeAnswered_IsInTheResultLine(bool stoodDown, string detail, string expected)
    {
        using var rig = new ClassSyncRig(pcs: 1, homes: 1);
        using var lab = new HandOverLab(rig, openHost: false);
        await lab.MakeTheOldHomeTheHomeAsync();
        using var home = new ScriptedHandOverHome(rig, channel =>
        {
            SendManifest(channel, new[] { Turma });
            Done(channel);
        });
        home.DoneAnswer = (stoodDown, detail);

        var result = await lab.Taker.TakeOverAsync(lab.Home with { Port = home.Port });

        Assert.True(result.Ok, result.Detail);
        Assert.Equal("As pastas das turmas agora ficam neste computador: 1 turma com 0 arquivos. " + expected, result.Detail);
    }

    // The home's Master doesn't answer (that PC is gone): after the window's
    // confirmation, this PC claims the Agents with a newer generation and
    // starts with its own classes, which stay where they are, and the class
    // of the lesson the room follows, for what the PCs set aside for it.
    [Fact]
    public async Task WithTheHomeUnreachable_TheTakeOverClaimsTheAgents_AndKeepsThisPcsOwnClasses()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig, openHost: false);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.GiveTheNewHomeAClassOfItsOwn();
        using var remote = new RemoteClassFolderController(
            rig.OtherMaster, () => lab.Home, () => rig.Machines, machine => !rig.Offline.ContainsKey(machine.Name),
            TimeSpan.FromHours(1), rig.AgentPort, () => rig.Now);
        for (var poll = 0; poll < RemoteClassFolderController.ClosedAfter; poll++)
        {
            await remote.PollAsync();
        }
        Assert.True(remote.HomeClosed);

        // A window that asked about a home answering does nothing now that it is closed.
        var mismatch = await remote.TakeOverAsync(lab.Taker, remote.Home, homeClosed: false);
        Assert.False(mismatch.Ok);
        Assert.Equal(OldHome, rig.Pcs[0].Agent.CurrentHome!.Name);

        var result = await remote.TakeOverAsync(lab.Taker, remote.Home, homeClosed: true);

        Assert.True(result.Ok, result.Detail);
        Assert.Equal(ClassFolderText.TookOverUnreachable, result.Detail);
        foreach (var pc in rig.Pcs)
        {
            Assert.Equal((NewHome, generation + 1), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation));
        }
        Assert.Equal(generation + 1, ClassHomeFileMaster.Read(lab.NewHomePath));
        // Its own class stays; the class of the lesson the room followed is made, empty, for what the PCs set aside.
        Assert.Equal(new[] { Turma, "Antiga" }, lab.NewStore.List().Select(summary => summary.Turma));
        Assert.Empty(lab.NewStore.Files(Turma));
        Assert.Equal(Turma, lab.NewStore.TurmaOfLesson(rig.Pcs[0].Agent.Current.LessonId));
        Assert.False(lab.NewStore.RanLesson(rig.Pcs[0].Agent.Current.LessonId));
        Assert.False(Directory.EnumerateDirectories(lab.NewRoot, "Turmas (*").Any());
        Assert.Equal(new[] { Turma, "3º B" }, rig.Store.List().Select(summary => summary.Turma));
    }

    // No Agent keeps a home now (the room's PCs were set up
    // again, say), but this PC saw PROF-1 as the home once, so its starts are
    // refused and its window works PROF-1, which doesn't answer. The take-over
    // claims the Agents with now's generation, and this PC starts lessons from then on.
    [Fact]
    public async Task ALastSeenHomeThatIsGone_IsTakenOver_AndThisPcStartsLessonsAfter()
    {
        using var rig = new ClassSyncRig(pcs: 2, homes: 2);
        using var lab = new HandOverLab(rig, openHost: false);
        ClassHomeFileMaster.WriteLastOtherHome(
            new ClassHome(rig.Master.Thumbprint, OldHome, "127.0.0.1", lab.ClosedPort, rig.NowTicks - 1000), lab.NewHomePath);
        Assert.True(lab.NewStore.Create("Antiga").Ok);
        await lab.NewSync.StartAsync("Antiga");
        Assert.False(lab.NewSync.Lesson.IsOn);

        var result = await lab.Taker.TakeOverUnreachableAsync(OldHome);

        Assert.True(result.Ok, result.Detail);
        Assert.All(rig.Pcs, pc => Assert.Equal((NewHome, rig.NowTicks), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
        Assert.Equal(rig.NowTicks, ClassHomeFileMaster.Read(lab.NewHomePath));
        var started = await lab.NewSync.StartAsync("Antiga");
        Assert.All(started, line => Assert.True(line.Ok, $"{line.Machine.Name}: {line.Detail}"));
        Assert.True(lab.NewSync.Lesson.IsOn);
    }

    // With a lesson on the Agents, an unreachable home's folders can't be taken either.
    [Fact]
    public async Task WithTheHomeUnreachable_AndALessonOn_TheTakeOverIsRefused()
    {
        using var rig = new ClassSyncRig(pcs: 2, homes: 2);
        using var lab = new HandOverLab(rig, openHost: false);
        await lab.MakeTheOldHomeTheHomeAsync();
        await lab.OldSync.StartAsync("3º B");
        var before = lab.Snapshot();

        var result = await lab.Taker.TakeOverUnreachableAsync(OldHome);

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverUnreachableFailed(ClassFolderText.EndLessonFirst), result.Detail);
        lab.AssertUnchanged(before);
        Assert.Null(ClassHomeFileMaster.Read(lab.NewHomePath));
    }

    [Fact]
    public void TheWindowsTexts_AreTheOnesAgreed()
    {
        Assert.Equal("Guardar as pastas das turmas neste computador", ClassFolderText.TakeOverButton);
        Assert.Equal(
            "O PC PROF-1 não responde. Guardar as pastas neste computador assim mesmo? As turmas que estão nele não vêm junto; "
            + "os arquivos que ainda estão nos PCs dos alunos vêm na próxima aula.",
            ClassFolderText.TakeOverUnreachableQuestion("PROF-1"));
        Assert.Equal("Encerre a aula antes de passar as pastas.", ClassFolderText.EndLessonFirst);
        var question = ClassFolderText.TakeOverQuestion("PROF-1", 3, 1234, 5L * 1024 * 1024, "PROF-2", ownClasses: true);
        Assert.Contains("do PC PROF-1", question);
        Assert.Contains("3 turmas", question);
        Assert.Contains("1.234 arquivos", question);
        Assert.Contains("Turmas (passadas para PROF-2)", question);
        Assert.Contains("Turmas (antes de", question);
        Assert.DoesNotContain("antes de", ClassFolderText.TakeOverQuestion("PROF-1", 1, 1, 10, "PROF-2", ownClasses: false));
    }

    // Each script breaks the copy in one way.
    private static void Hostile(SecureChannel channel, string script)
    {
        var a = $"files/{Turma}/a.txt";
        switch (script)
        {
            case "path out of the store":
                SendManifest(channel, new[] { Turma }, ($"files/{Turma}/../../../fora.txt", 3, Ticks));
                SendFile(channel, $"files/{Turma}/../../../fora.txt", new byte[3]);
                break;
            case "path out of the class":
                SendManifest(channel, new[] { Turma }, ("files/../fora.txt", 3, Ticks));
                SendFile(channel, "files/../fora.txt", new byte[3]);
                break;
            case "path the manifest doesn't have":
                SendManifest(channel, new[] { Turma }, (a, 3, Ticks));
                SendFile(channel, $"files/{Turma}/../fora.txt", new byte[3]);
                break;
            case "header size":
                SendManifest(channel, new[] { Turma }, (a, 3, Ticks));
                SendFile(channel, a, new byte[5]);
                break;
            case "more bytes":
                SendManifest(channel, new[] { Turma }, (a, 3, Ticks));
                SendFile(channel, a, new byte[5], announced: 3);
                break;
            case "fewer bytes":
                SendManifest(channel, new[] { Turma }, (a, 3, Ticks));
                SendFile(channel, a, new byte[2], announced: 3);
                break;
            case "a file missing":
                SendManifest(channel, new[] { Turma }, (a, 3, Ticks), ($"files/{Turma}/b.txt", 3, Ticks));
                SendFile(channel, a, new byte[3]);
                break;
            case "a class not listed":
                SendManifest(channel, new[] { Turma }, ("files/9º Z/fora.txt", 3, Ticks));
                SendFile(channel, "files/9º Z/fora.txt", new byte[3]);
                break;
            case "the same path twice":
                SendManifest(channel, new[] { Turma }, (a, 3, Ticks), ($"files/{Turma}/A.TXT", 3, Ticks));
                SendFile(channel, a, new byte[3]);
                SendFile(channel, $"files/{Turma}/A.TXT", new byte[3]);
                break;
            case "a file and a folder":
                SendManifest(channel, new[] { Turma }, (a, 3, Ticks), ($"files/{Turma}/a.txt/fora.txt", 3, Ticks));
                SendFile(channel, a, new byte[3]);
                SendFile(channel, $"files/{Turma}/a.txt/fora.txt", new byte[3]);
                break;
            case "an index too big":
                SendManifest(channel, new[] { Turma }, ($"index/{Turma}.json", ClassFolderHandOver.MaxDataFileBytes + 1, Ticks));
                break;
            case "a bad class name":
                SendManifest(channel, new[] { "../fora" }, ("files/../fora/a.txt", 3, Ticks));
                break;
            case "a bad time":
                SendManifest(channel, new[] { Turma }, (a, 3, -1));
                SendFile(channel, a, new byte[3]);
                break;
            case "a manifest that isn't one":
                SendFile(channel, Manifest, Encoding.UTF8.GetBytes("{\"classes\": 3}"));
                break;
            case "no manifest":
                SendFile(channel, a, new byte[3]);
                break;
        }
        Done(channel);
    }

    private static void SendManifest(SecureChannel channel, string[] classes, params (string Path, long Size, long Ticks)[] files) =>
        SendFile(channel, Manifest, ClassFolderHandOver.EncodeManifest(classes, files));

    // A SendFile header announcing the size (the content's length unless told
    // otherwise), then the content in FileChunks.
    private static void SendFile(SecureChannel channel, string path, byte[] content, long? announced = null)
    {
        channel.Send(new Message(MessageType.SendFile, FileTransfer.EncodeHeader(path, announced ?? content.Length)));
        for (var offset = 0; offset < content.Length; offset += FileTransfer.ChunkSize)
        {
            channel.Send(new Message(MessageType.FileChunk, content.AsSpan(offset, Math.Min(FileTransfer.ChunkSize, content.Length - offset)).ToArray()));
        }
    }

    private static void Done(SecureChannel channel) =>
        channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(true, "pronto")));

    // A home's Master that answers RemoteHandOver with a script and
    // RemoteHandOverDone with a yes, holding the home's certificate (so the
    // remote's pin is satisfied), on loopback.
    private sealed class ScriptedHandOverHome : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly ClassSyncRig _rig;
        private readonly Action<SecureChannel> _script;
        private readonly Task _serving;

        public ScriptedHandOverHome(ClassSyncRig rig, Action<SecureChannel> script)
        {
            _rig = rig;
            _script = script;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serving = OwnThread(Serve);
        }

        public int Port { get; }

        // What it answers RemoteHandOverDone: stood down, with no classes to put aside, by default.
        public (bool Ok, string Detail) DoneAnswer { get; set; } = (true, string.Empty);

        // What it answers RemoteTakeBack: refused, by default.
        public (bool Ok, string Detail) TakeBackAnswer { get; set; } = (false, string.Empty);

        public void Dispose()
        {
            _listener.Stop();
            _serving.Wait(TimeSpan.FromSeconds(10));
        }

        private void Serve()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
                {
                    return; // the test is over
                }

                using (client)
                {
                    try
                    {
                        using var channel = SecureChannel.AuthenticateAsServerAcceptingAnyClient(client, _rig.Master);
                        var request = channel.Receive();
                        if (request.Type is MessageType.RemoteHandOverDone or MessageType.RemoteTakeBack)
                        {
                            var (ok, detail) = request.Type == MessageType.RemoteTakeBack ? TakeBackAnswer : DoneAnswer;
                            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(ok, detail)));
                            continue;
                        }
                        _script(channel);
                        // Until the other side lets go: closing first could cut off what was sent.
                        channel.Receive();
                    }
                    catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidDataException
                        or System.Security.Authentication.AuthenticationException)
                    {
                        // The taker hung up (as it should on a hostile copy), or let go at the end.
                    }
                }
            }
        }
    }
}
