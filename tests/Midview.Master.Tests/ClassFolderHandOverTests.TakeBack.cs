using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// ClassFolderHandOver: a take-over that can't be told done (an Agent that may
// have taken the claim doesn't answer) settled through the old home (Ruling
// 21): this PC asks it to take the room back (MessageType.RemoteTakeBack) and,
// once it has, puts its own classes back and throws the copy away. Only when
// the old home can't be reached, or refuses, does the copy stay (Ruling 20).
public sealed partial class ClassFolderHandOverTests
{
    // PC1 took the claim and went off before answering. The old home takes the
    // PCs it reaches back, newer than the take-over; PC1, back with this PC at
    // the take-over's generation, is an older straggler: no PC runs on the copy.
    [Fact]
    public async Task AnUndecidedTakeOver_IsTakenBackByTheOldHome_AndNoPcRunsOnTheCopy()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        var oldFiles = lab.FilesUnder(Path.Combine(rig.Root, "Turmas"));
        lab.PutALessonOnTheAgentOf(rig.Pcs[1]);
        lab.PutALessonOnTheAgentOf(rig.Pcs[2]);
        lab.LetPc1TakeTheClaimAndGoOff(takes: true);

        var result = await lab.Taker.TakeOverAsync(lab.Home);
        rig.Offline.Clear(); // PC1 is back

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverTakenBack(OldHome, new[] { "PC1" }), result.Detail);
        // This PC: its own classes back, the copy gone, no generation of its own.
        Assert.Equal(new[] { "Antiga" }, lab.NewStore.List().Select(summary => summary.Turma));
        Assert.False(Directory.EnumerateDirectories(lab.NewRoot, "Turmas (*").Any());
        Assert.Null(ClassHomeFileMaster.Read(lab.NewHomePath));
        lab.AssertNoStaging();
        // The old home: newer than the take-over on the PCs it reached, its store as it was.
        var takenBack = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        Assert.True(takenBack > generation + 1);
        Assert.All(rig.Pcs.Skip(1), pc => Assert.Equal((OldHome, takenBack), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
        Assert.Equal((NewHome, generation + 1), (rig.Pcs[0].Agent.CurrentHome!.Name, rig.Pcs[0].Agent.CurrentHome!.Generation));
        Assert.Equal(oldFiles, lab.FilesUnder(Path.Combine(rig.Root, "Turmas")));
        // Each PC's view: the old home is the home.
        using var taker = lab.TrackerOf(rig.OtherMaster, NewHome, lab.NewHomePath);
        using var old = lab.TrackerOf(rig.Master, OldHome, rig.HomePath);
        await taker.RefreshAsync();
        await old.RefreshAsync();
        Assert.Equal(ClassFolderMode.Remote, taker.Mode);
        Assert.Equal((OldHome, takenBack), (taker.Home!.Name, taker.Home.Generation));
        Assert.Equal(ClassFolderMode.Local, old.Mode);
    }

    // The old home refuses to take the room back when it isn't the home any
    // more (no Agent it reaches names it, here): Ruling 20's way, the copy
    // stays. Told apart from a take-back that couldn't be asked: the old home
    // answered, saying why.
    [Fact]
    public async Task ATakeBackTheOldHomeRefuses_LeavesTheCopyInPlace()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var homes = lab.Homes();
        lab.FillTheOldHome();
        lab.GiveTheNewHomeAClassOfItsOwn();
        lab.PutALessonOnTheAgentOf(rig.Pcs[1]);
        lab.PutALessonOnTheAgentOf(rig.Pcs[2]);
        var logged = new System.Collections.Concurrent.ConcurrentQueue<string>();
        lab.TakerLog = logged.Enqueue;
        lab.LetPc1TakeTheClaimAndGoOff(
            takes: false, whenGone: () => lab.Giver.ReadHome = _ => throw new IOException("the old home reaches no Agent"));

        var result = await lab.Taker.TakeOverAsync(lab.Home);

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverUndecided(OldHome, new[] { "PC1" }, OwnAside), result.Detail);
        Assert.Contains(logged, line => line.EndsWith("didn't take the room back: O PC PROF-1 não guarda mais as pastas das turmas.", StringComparison.Ordinal));
        Assert.DoesNotContain(logged, line => line.Contains("couldn't be asked to take the room back", StringComparison.Ordinal));
        Assert.Equal(new[] { Turma, "3º B", "Vídeos" }, lab.NewStore.List().Select(summary => summary.Turma));
        Assert.True(File.Exists(Path.Combine(lab.NewRoot, OwnAside, "Antiga", "velho.txt")));
        Assert.Null(ClassHomeFileMaster.Read(lab.NewHomePath));
        Assert.Equal(homes, lab.Homes());
    }

    // RemoteTakeBack from a PC the home doesn't serve, with no generation, or
    // to a PC that isn't the home any more: refused, and nothing changes.
    [Fact]
    public async Task ATakeBackFromAStranger_OrNotAGeneration_OrToAPcNoLongerTheHome_IsRefused()
    {
        using var rig = new ClassSyncRig(homes: 2, pcs: 2);
        using var lab = new HandOverLab(rig);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = ClassHomeFileMaster.Read(rig.HomePath)!.Value;
        var homes = rig.Pcs.Select(pc => pc.Agent.CurrentHome).ToList();
        using var stranger = SelfSignedCertificate.Create("stranger-under-test");

        Assert.Equal((false, ClassFolderRemote.NotTrustedText), lab.SendTakeBack(stranger, ClassFolderRemote.EncodeTakeBack(generation + 1)));
        Assert.Equal((false, ClassFolderHost.IncompleteText), lab.SendTakeBack(rig.OtherMaster, new byte[] { 1, 2, 3 }));
        Assert.Equal(homes, rig.Pcs.Select(pc => pc.Agent.CurrentHome));
        Assert.Equal(generation, ClassHomeFileMaster.Read(rig.HomePath));

        // Another PC really took the folders over meanwhile.
        foreach (var pc in rig.Pcs)
        {
            Assert.True(ClassFolderClient.ClaimHome(
                pc.Machine, rig.OtherMaster, new ClassHomeClaim(NewHome, ClassFolderRemote.Port, true, generation + 5), rig.AgentPort).Ok);
        }
        var (ok, _) = lab.SendTakeBack(rig.OtherMaster, ClassFolderRemote.EncodeTakeBack(generation + 1));

        Assert.False(ok);
        Assert.All(rig.Pcs, pc => Assert.Equal((NewHome, generation + 5), (pc.Agent.CurrentHome!.Name, pc.Agent.CurrentHome.Generation)));
        Assert.Equal(generation, ClassHomeFileMaster.Read(rig.HomePath));
    }

    // The home's Master closed (no copy came, so there is nothing to give
    // back, and nobody to ask): an undecided unreachable take-over leaves this
    // PC's own classes as they are and keeps no generation; when PC1 is back
    // naming this PC, this PC is the home with its own classes, as the teacher
    // chose when the home didn't answer.
    [Fact]
    public async Task AnUndecidedUnreachableTakeOver_LeavesThisPcsClasses_AndPc1Decides()
    {
        using var rig = new ClassSyncRig(homes: 3);
        using var lab = new HandOverLab(rig, openHost: false);
        await lab.MakeTheOldHomeTheHomeAsync();
        var generation = rig.Pcs[0].Agent.CurrentHome!.Generation;
        lab.GiveTheNewHomeAClassOfItsOwn();
        lab.PutALessonOnTheAgentOf(rig.Pcs[1]);
        lab.PutALessonOnTheAgentOf(rig.Pcs[2]);
        lab.LetPc1TakeTheClaimAndGoOff(takes: true);

        var result = await lab.Taker.TakeOverUnreachableAsync(OldHome);
        rig.Offline.Clear();

        Assert.False(result.Ok);
        Assert.Equal(ClassFolderText.TakeOverUnreachableUndecided(new[] { "PC1" }), result.Detail);
        Assert.Equal(new[] { "Antiga" }, lab.NewStore.List().Select(summary => summary.Turma));
        Assert.False(Directory.EnumerateDirectories(lab.NewRoot, "Turmas (*").Any());
        Assert.Null(ClassHomeFileMaster.Read(lab.NewHomePath));
        using var tracker = lab.TrackerOf(rig.OtherMaster, NewHome, lab.NewHomePath);
        await tracker.RefreshAsync();
        Assert.Equal(ClassFolderMode.Local, tracker.Mode);
        Assert.Equal((NewHome, generation + 1), (tracker.Home!.Name, tracker.Home.Generation));
    }

    [Fact]
    public void TheUndecidedAndTakenBackTexts_AreTheOnesAgreed()
    {
        Assert.Equal(
            "Ainda não dá para saber qual computador guarda as pastas: o PC PC1 pode ter aceitado este computador e não responde "
            + "agora. As turmas copiadas do PC PROF-1 estão na pasta Turmas deste computador, e as que já estavam aqui estão na "
            + "pasta \"Turmas (antes de 20-10-2026 1330)\". Nada foi apagado: quando esse PC voltar, os PCs da sala decidem qual "
            + "computador guarda as pastas.",
            ClassFolderText.TakeOverUndecided("PROF-1", new[] { "PC1" }, OwnAside));
        Assert.Equal(
            "Ainda não dá para saber qual computador guarda as pastas: os PCs PC1, PC2 podem ter aceitado este computador e não "
            + "respondem agora. As turmas copiadas do PC PROF-1 estão na pasta Turmas deste computador. Nada foi apagado: quando "
            + "esses PCs voltarem, os PCs da sala decidem qual computador guarda as pastas.",
            ClassFolderText.TakeOverUndecided("PROF-1", new[] { "PC1", "PC2" }, null));
        Assert.Equal(
            "Ainda não dá para saber qual computador guarda as pastas: os PCs PC1, PC2, PC3, PC4, PC5 e mais 2 podem ter aceitado "
            + "este computador e não respondem agora. As turmas deste computador continuam onde estão. Quando esses PCs voltarem, "
            + "os PCs da sala decidem qual computador guarda as pastas.",
            ClassFolderText.TakeOverUnreachableUndecided(new[] { "PC1", "PC2", "PC3", "PC4", "PC5", "PC6", "PC7" }));
        Assert.Equal(
            "As pastas das turmas continuam no PC PROF-1: o PC PC1 não confirmou a troca. Tente de novo.",
            ClassFolderText.TakeOverTakenBack("PROF-1", new[] { "PC1" }));
        Assert.Equal(
            "As pastas das turmas continuam no PC PROF-1: os PCs PC1, PC2 não confirmaram a troca. Tente de novo.",
            ClassFolderText.TakeOverTakenBack("PROF-1", new[] { "PC1", "PC2" }));
        Assert.Equal("O PC PROF-1 não guarda mais as pastas das turmas.", ClassFolderText.NotTheHomeAnyMore("PROF-1"));
    }

    private sealed partial class HandOverLab
    {
        // PC1's Agent takes the claim (or not) and its PC goes off before it
        // answers: no answer to the claim (after it went out), none when this
        // PC asks again which home it keeps, and the grid shows it off (so the
        // old home doesn't reach it either) until the test says it is back.
        public void LetPc1TakeTheClaimAndGoOff(bool takes, Action? whenGone = null)
        {
            var gone = 0;
            Taker.SendClaim = (machine, claim, sending) =>
            {
                if (machine.Name != "PC1")
                {
                    return ClassFolderClient.ClaimHome(machine, _rig.OtherMaster, claim, _rig.AgentPort, sending);
                }
                sending();
                if (takes)
                {
                    ClassFolderClient.ClaimHome(machine, _rig.OtherMaster, claim, _rig.AgentPort);
                }
                Volatile.Write(ref gone, 1);
                _rig.Offline["PC1"] = true;
                whenGone?.Invoke();
                throw new IOException("PC1 went off before it answered");
            };
            Taker.ReadHome = machine => machine.Name == "PC1" && Volatile.Read(ref gone) == 1
                ? throw new IOException("PC1 is off")
                : ClassFolderClient.GetHome(machine, _rig.OtherMaster, _rig.AgentPort);
        }

        public ClassHomeTracker TrackerOf(System.Security.Cryptography.X509Certificates.X509Certificate2 master, string name, string homePath) =>
            new(master, name, () => _rig.Machines, machine => !_rig.Offline.ContainsKey(machine.Name), TimeSpan.FromHours(1),
                _rig.AgentPort, homePath);

        public List<(string Path, long Length)> FilesUnder(string folder) =>
            Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(path => (Path.GetRelativePath(folder, path), new FileInfo(path).Length)).Order().ToList();

        // RemoteTakeBack, from sender's PC, to the old home's host.
        public (bool Ok, string Detail) SendTakeBack(System.Security.Cryptography.X509Certificates.X509Certificate2 sender, byte[] payload)
        {
            using var tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, Host!.Port);
            using var channel = SecureChannel.AuthenticateAsClient(tcp, "localhost", sender, new TrustStore(new[] { _rig.Master.Thumbprint }));
            channel.Send(new Message(MessageType.RemoteTakeBack, payload));
            var reply = channel.Receive();
            Assert.Equal(MessageType.CommandAck, reply.Type);
            return CommandResult.Decode(reply.Payload);
        }
    }
}
