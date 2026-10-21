using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Midview.Agent;
using Midview.Master;
using Midview.Protocol;
using Midview.Session;
using Xunit;

// A lab for ClassFolderSync: the teacher's store, lesson file and Recycle Bin
// in temp folders, and student PCs, each a real AgentServer with a
// ClassLessonHandler (no student logged on, so no folder or registry is
// touched by it) and a real SessionServer with a temp ClassFolder that follows
// that Agent's lesson. Each PC has its own loopback address (127.0.0.2, .3,
// .4...) so they all answer on the same two ports, as lab PCs do. One clock
// (Now) for the teacher's PC and the Agents. Never a lab machine, the real
// Desktop, the real registry or the teacher's real data.
internal sealed class ClassSyncRig : IDisposable
{
    public const string Turma = "3º A";

    private readonly List<ClassFolderSync> _syncs = new();

    public ClassSyncRig(int pcs = 3)
    {
        Root = Path.Combine(Path.GetTempPath(), $"midview-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        TeacherBin = new FolderBin(Path.Combine(Root, "lixeira-professor"));
        Store = new ClassStore(
            Path.Combine(Root, "Turmas"), Path.Combine(Root, "dados", "turmas"), TeacherBin, () => Now,
            lessonsPath: Path.Combine(Root, "dados", "class-lessons.json"), freeBytes: () => FreeSpace);
        Assert.True(Store.Create(Turma).Ok);
        Assert.True(Store.Create("3º B").Ok);

        var addresses = Enumerable.Range(2, pcs).Select(last => IPAddress.Parse($"127.0.0.{last}")).ToList();
        SessionPort = PortFreeOnAll(addresses);
        AgentPort = PortFreeOnAll(addresses);
        for (var number = 1; number <= pcs; number++)
        {
            Pcs.Add(new StudentPc(this, number, addresses[number - 1]));
        }
    }

    // The teacher's clock, and the Agents': the day a lesson started, the day files arrived.
    public DateTime Now { get; set; } = new(2026, 10, 20, 13, 30, 0);

    // What the teacher's disk has left, as the store sees it (never the real disk's).
    public long FreeSpace { get; set; } = long.MaxValue;

    public string Root { get; }

    public X509Certificate2 Master { get; } = SelfSignedCertificate.Create("master-under-test");

    public ClassStore Store { get; }

    public FolderBin TeacherBin { get; }

    public string LessonPath => Path.Combine(Root, "dados", "class-lesson.json");

    public int SessionPort { get; }

    public int AgentPort { get; }

    public List<StudentPc> Pcs { get; } = new();

    // The PCs isOnline says no for.
    public ConcurrentDictionary<string, bool> Offline { get; } = new();

    public IReadOnlyList<KnownMachine> Machines => Pcs.Select(pc => pc.Machine).ToList();

    // An hour between rounds by default, so only the test runs them.
    public ClassFolderSync NewSync(TimeSpan? interval = null)
    {
        var sync = new ClassFolderSync(
            Store, Master, () => Machines, machine => !Offline.ContainsKey(machine.Name), () => Now,
            AgentPort, SessionPort, interval ?? TimeSpan.FromHours(1), LessonPath);
        _syncs.Add(sync);
        return sync;
    }

    // The last status of each PC, by name.
    public static ConcurrentDictionary<string, PcStatus> Watch(ClassFolderSync sync)
    {
        var statuses = new ConcurrentDictionary<string, PcStatus>();
        sync.StatusChanged += status => statuses[status.Machine.Name] = status;
        return statuses;
    }

    // Rounds over every online PC, one PC after the other, in order.
    public async Task Rounds(ClassFolderSync sync, int count)
    {
        for (var round = 0; round < count; round++)
        {
            foreach (var pc in Pcs.Where(pc => !Offline.ContainsKey(pc.Name)))
            {
                await sync.RunRoundAsync(pc.Machine, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));
            }
        }
    }

    public string StorePath(string relativePath, string turma = Turma) =>
        Path.Combine(Root, "Turmas", turma, relativePath.Replace('/', Path.DirectorySeparatorChar));

    // A file the teacher put in the class folder in Explorer.
    public void WriteInStore(string relativePath, byte[] content, DateTime utc, string turma = Turma) =>
        WriteFile(StorePath(relativePath, turma), content, utc);

    public string[] StoreFiles(string turma = Turma) =>
        Store.Files(turma).Keys.Order(StringComparer.Ordinal).ToArray();

    public static void WriteFile(string path, byte[] content, DateTime utc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, utc);
    }

    public void Dispose()
    {
        foreach (var sync in _syncs)
        {
            sync.Dispose();
        }
        foreach (var pc in Pcs)
        {
            pc.Dispose();
        }
        Master.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A server still letting go of a file: the temp folder is the system's to clean.
        }
    }

    // A port nothing listens on at any of the addresses.
    private static int PortFreeOnAll(IReadOnlyList<IPAddress> addresses)
    {
        for (var attempt = 0; ; attempt++)
        {
            var first = new TcpListener(addresses[0], 0);
            first.Start();
            var port = ((IPEndPoint)first.LocalEndpoint).Port;
            var held = new List<TcpListener> { first };
            try
            {
                foreach (var address in addresses.Skip(1))
                {
                    var listener = new TcpListener(address, port);
                    listener.Start();
                    held.Add(listener);
                }
                return port;
            }
            catch (SocketException) when (attempt < 20)
            {
                // Taken on one of them: another port.
            }
            finally
            {
                foreach (var listener in held)
                {
                    listener.Stop();
                }
            }
        }
    }
}

// One student PC of the rig.
internal sealed class StudentPc : IDisposable
{
    private readonly X509Certificate2 _agentCertificate = SelfSignedCertificate.Create("agent-under-test");
    private readonly X509Certificate2 _sessionCertificate = SelfSignedCertificate.Create("session-under-test");
    private readonly ThumbnailPollerTests.LoopbackSession _session;
    private readonly TcpListener _agentListener;
    private readonly CancellationTokenSource _stopAgent = new();
    private readonly Task _agentRun;

    public StudentPc(ClassSyncRig rig, int number, IPAddress address)
    {
        Name = $"PC{number}";
        Root = Path.Combine(rig.Root, Name);
        Desktop = Path.Combine(Root, "Desktop");
        Directory.CreateDirectory(Desktop);
        Bin = new FolderBin(Path.Combine(Root, "lixeira"));

        Agent = new ClassLessonHandler(
            Path.Combine(Root, "class-lesson.json"), () => null, _ => null, new AppBlockEnforcer(new NoProcesses()),
            WindowsIdentity.GetCurrent().User!, () => rig.Now, _ => { });
        _agentListener = new TcpListener(address, rig.AgentPort);
        _agentListener.Start();
        var siteBlock = new SiteBlockCommandHandler(
            new SiteBlockState(), Path.Combine(Root, "sites.json"), Path.Combine(Root, "hosts"), new NoFirewall());
        var agent = new AgentServer(
            _agentListener, _agentCertificate, new TrustStore(new[] { rig.Master.Thumbprint }), siteBlock, classLesson: Agent);
        _agentRun = Task.Run(() => agent.Run(_stopAgent.Token));

        Folder = new ClassFolder(Desktop, Path.Combine(Root, "guardada"), SessionLesson, Bin, Path.Combine(Root, "session.json"));
        _session = new ThumbnailPollerTests.LoopbackSession(
            rig.SessionPort, _sessionCertificate, rig.Master, classFolder: Folder, address: address);
        Machine = new KnownMachine(Name, address.ToString(), _sessionCertificate.Thumbprint, _agentCertificate.Thumbprint);
    }

    public string Name { get; }

    public string Root { get; }

    public string Desktop { get; }

    public FolderBin Bin { get; }

    public ClassLessonHandler Agent { get; }

    public ClassFolder Folder { get; }

    public KnownMachine Machine { get; }

    // What the Session takes for the Agent's lesson instead of the Agent's own (a PC whose Session is behind).
    public ClassLesson? SessionLessonOverride { get; set; }

    // The Desktop folder of the lesson the Agent is on.
    public string LessonFolder => Path.Combine(Desktop, Agent.Current.FolderName);

    public string PathOf(string relativePath) => Path.Combine(LessonFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void Write(string relativePath, byte[] content, DateTime utc) => ClassSyncRig.WriteFile(PathOf(relativePath), content, utc);

    public bool Has(string relativePath) => File.Exists(PathOf(relativePath));

    public byte[] Read(string relativePath) => File.ReadAllBytes(PathOf(relativePath));

    public DateTime TimeOf(string relativePath) => File.GetLastWriteTimeUtc(PathOf(relativePath));

    public string[] Files() => Directory.Exists(LessonFolder)
        ? Directory.GetFiles(LessonFolder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(LessonFolder, file).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray()
        : Array.Empty<string>();

    // What the Session does every few seconds: follow the lesson, setting a finished one's files aside.
    public void Follow() => Folder.Follow(null);

    // The Agent makes the lesson's folder on the student's Desktop; here, the Session's view of the lesson does.
    private ClassLesson? SessionLesson()
    {
        var lesson = SessionLessonOverride ?? Agent.Current;
        if (lesson.IsOn)
        {
            Directory.CreateDirectory(Path.Combine(Desktop, lesson.FolderName));
        }
        return lesson;
    }

    public void Dispose()
    {
        _session.Dispose();
        _stopAgent.Cancel();
        _agentListener.Stop();
        try
        {
            _agentRun.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Stopping the listener under the server.
        }
        _agentCertificate.Dispose();
        _sessionCertificate.Dispose();
    }

    private sealed class NoProcesses : IProcessInspector
    {
        public IReadOnlyList<RunningProcess> ListInteractive() => Array.Empty<RunningProcess>();
        public ProcessFacts? Inspect(RunningProcess process) => null;
        public bool End(RunningProcess process) => true;
        public void Notify(int sessionId, string message) { }
    }

    private sealed class NoFirewall : IFirewallController
    {
        public bool ApplyBlock() => true;
        public void ApplyUnblock() { }
    }
}

// A Recycle Bin that is a folder: each thing recycled goes into a numbered folder of its own.
internal sealed class FolderBin : IRecycleBin
{
    private readonly string _folder;
    private int _count;

    public FolderBin(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    // The files in the bin, by name, whatever folder they went in with.
    public string[] FileNames() => Directory.GetFiles(_folder, "*", SearchOption.AllDirectories)
        .Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal).ToArray();

    public byte[] Read(string name) => File.ReadAllBytes(
        Directory.GetFiles(_folder, name, SearchOption.AllDirectories).Single());

    public void Recycle(string path)
    {
        var holder = Path.Combine(_folder, Interlocked.Increment(ref _count).ToString());
        Directory.CreateDirectory(holder);
        var target = Path.Combine(holder, Path.GetFileName(path));
        if (Directory.Exists(path))
        {
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target);
        }
    }
}
