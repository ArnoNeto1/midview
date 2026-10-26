using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Master;
using Midview.Protocol;

public sealed partial class ClassFolderRoleTests
{
    // One teacher PC's class folder side, as MainWindow puts it together: its
    // store (PROF-1: the rig's own; another name: one of its own under the
    // rig's root), sync, local controller, hand-over, tracker and role. Its
    // host listens on 127.0.0.1, on a port of its own that its claims carry
    // (the Agents keep it), and its remote controller reaches the home there
    // (the Agents keep the address a claim came from). Both of the rig's
    // teachers are colleagues. trackerInterval and confirmWait: long ones by
    // default, so only the test's own refreshes are readings; short ones make
    // the tracker read the room by itself, as in a lab. The role's EndedGrace
    // runs by a clock the test moves (MoveClock). FailedOpens: how many times
    // the host can't be had first.
    private sealed class TeacherPc : IDisposable
    {
        public TeacherPc(
            ClassSyncRig rig, string name, X509Certificate2 certificate, TimeSpan? confirmWait = null,
            TimeSpan? trackerInterval = null)
        {
            HostPort = FreePort();
            var own = name == Prof1 ? rig.Root : Path.Combine(rig.Root, name.ToLowerInvariant());
            Store = name == Prof1
                ? rig.Store
                : new ClassStore(
                    Path.Combine(own, "Turmas"), Path.Combine(own, "dados", "turmas"), new FolderBin(Path.Combine(own, "lixeira")),
                    () => rig.Now, lessonsPath: Path.Combine(own, "dados", "class-lessons.json"));
            var homePath = Path.Combine(own, "dados", "class-home.json");
            IReadOnlyList<KnownMachine> Machines() => rig.Machines;
            bool IsOnline(KnownMachine machine) => !rig.Offline.ContainsKey(machine.Name);

            Sync = new ClassFolderSync(
                Store, certificate, Machines, IsOnline, () => rig.Now, name, agentPort: rig.AgentPort, sessionPort: rig.SessionPort,
                homePort: HostPort, interval: TimeSpan.FromHours(1), lessonPath: Path.Combine(own, "dados", "class-lesson.json"),
                homePath: homePath);
            Local = new LocalClassFolderController(Store, Sync, Machines, IsOnline, () => rig.Now, name);
            HandOver = new ClassFolderHandOver(
                Store, Sync, certificate, name, Machines, IsOnline, () => rig.Now, rig.AgentPort, HostPort, homePath)
            {
                // The rig's Agents wait StudentPc.ClaimWait for their lock, not ClassHomeClaim.MaxLockWait.
                LostAnswerWait = StudentPc.ClaimWait + TimeSpan.FromMilliseconds(500),
            };
            var tracker = Tracker = new ClassHomeTracker(
                certificate, name, Machines, IsOnline, trackerInterval ?? TimeSpan.FromHours(1), rig.AgentPort, homePath);
            Role = new ClassFolderRole(
                tracker, Sync, Local, HandOver,
                () =>
                {
                    Interlocked.Increment(ref _opens);
                    if (Interlocked.Decrement(ref _failedOpens) >= 0)
                    {
                        return null; // the port taken just now
                    }
                    Interlocked.Increment(ref _opened);
                    return ClassFolderHost.Open(
                        certificate, Local,
                        (thumbprint, _) => Task.FromResult(
                            string.Equals(thumbprint, rig.Master.Thumbprint, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(thumbprint, rig.OtherMaster.Thumbprint, StringComparison.OrdinalIgnoreCase)),
                        HostPort, _ => { }, IPAddress.Loopback, HandOver);
                },
                () => new RemoteClassFolderController(
                    certificate, () => tracker.Home is { } home ? home with { Address = IPAddress.Loopback.ToString() } : null,
                    Machines, IsOnline, TimeSpan.FromHours(1), rig.AgentPort, () => rig.Now),
                () => ClassLessonOnAgents.Newest(
                    Machines().Where(IsOnline).Select(machine => ClassLessonOnAgents.Read(machine, certificate, rig.AgentPort, _ => { })),
                    new DateTimeOffset(rig.Now).ToUnixTimeSeconds()),
                confirmWait ?? TimeSpan.FromHours(1))
            {
                HostRetryWait = TimeSpan.FromMilliseconds(200),
                Milliseconds = () => Environment.TickCount64 + Interlocked.Read(ref _clockAhead),
            };
        }

        private int _failedOpens;
        private int _opens;
        private int _opened;
        private long _clockAhead;

        // Moves the clock the role's EndedGrace runs by.
        public void MoveClock(TimeSpan by) => Interlocked.Add(ref _clockAhead, (long)by.TotalMilliseconds);

        public int FailedOpens
        {
            set => Volatile.Write(ref _failedOpens, value);
        }

        // How many times the role tried to open the host, and how many of those opened it.
        public int Opens => Volatile.Read(ref _opens);

        public int Opened => Volatile.Read(ref _opened);

        public int HostPort { get; }

        public ClassStore Store { get; }

        public ClassFolderSync Sync { get; }

        public LocalClassFolderController Local { get; }

        public ClassFolderHandOver HandOver { get; }

        public ClassFolderRole Role { get; }

        // The role's (it owns it).
        public ClassHomeTracker Tracker { get; }

        public void Dispose()
        {
            Role.Dispose();
            Sync.Dispose();
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
