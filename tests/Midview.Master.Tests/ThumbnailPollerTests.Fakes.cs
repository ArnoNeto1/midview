using System.Drawing;
using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Midview.Session;

// ThumbnailPollerTests' stand-ins: a real Session on a loopback port, an
// Agent that answers every question the same way, and the fakes the
// Session runs with.
public partial class ThumbnailPollerTests
{
    // An accept loop, which blocks until its listener stops, on a thread of its
    // own: on the pool's, a loaded test run can leave it unstarted for longer
    // than a Master waits for an answer, and a rig's many loops would hold
    // pool threads the code under test needs.
    private static void OwnThread(Action loop) =>
        Task.Factory.StartNew(loop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // A real SessionServer on a loopback port, with a fixed-size fake screen.
    internal sealed class LoopbackSession : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _sessionCertificate;
        private readonly bool _ownsCertificates;

        public LoopbackSession()
            : this(0, SelfSignedCertificate.Create("session-under-test"), SelfSignedCertificate.Create("master-under-test"))
        {
            _ownsCertificates = true;
        }

        public LoopbackSession(
            int port,
            System.Security.Cryptography.X509Certificates.X509Certificate2 sessionCertificate,
            System.Security.Cryptography.X509Certificates.X509Certificate2 masterCertificate,
            IMessageDisplay? messageDisplay = null,
            IChatWindows? chatWindows = null,
            FileInbox? fileInbox = null,
            IAppCloser? appCloser = null,
            DeliveryFolder? deliveryFolder = null,
            IScreenLockDisplay? screenLock = null,
            ClassFolder? classFolder = null,
            IPAddress? address = null)
        {
            _listener = new TcpListener(address ?? IPAddress.Loopback, port);
            _sessionCertificate = sessionCertificate;
            MasterCertificate = masterCertificate;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var server = new SessionServer(
                _listener, _sessionCertificate, new TrustStore(new[] { MasterCertificate.Thumbprint }),
                new FixedSizeCapture(), new NoInput(),
                new InputLockManager(new NoDevices(), new SystemClock(), TimeSpan.FromSeconds(5)),
                messageDisplay, chatWindows, fileInbox, appCloser, deliveryFolder, screenLock: screenLock, classFolder: classFolder);
            OwnThread(() =>
            {
                try
                {
                    server.Run(CancellationToken.None);
                }
                catch (SocketException)
                {
                    // Expected: Dispose stops the listener under AcceptTcpClient().
                }
            });
        }

        public System.Security.Cryptography.X509Certificates.X509Certificate2 MasterCertificate { get; }

        public int Port { get; }

        public KnownMachine Machine() => new("Aluno-1", "127.0.0.1", _sessionCertificate.Thumbprint, null);

        public void Dispose()
        {
            _listener.Stop();
            if (_ownsCertificates)
            {
                _sessionCertificate.Dispose();
                MasterCertificate.Dispose();
            }
        }
    }

    // Answers every question on a loopback port with the same reply, and
    // keeps track of the GetWakeAddress and SetPowerSchedule ones.
    private sealed class FakeAgent : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _certificate;
        private readonly List<long> _schedulesReceived = new();
        private readonly List<long> _blocksReceived = new();
        private readonly List<long> _lessonsReceived = new();
        private readonly bool _ownsCertificate;
        private int _questions;

        // port and certificate: to stand in for an Agent that comes back where it was.
        public FakeAgent(
            System.Security.Cryptography.X509Certificates.X509Certificate2 masterCertificate, Message reply, int port = 0,
            System.Security.Cryptography.X509Certificates.X509Certificate2? certificate = null)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _ownsCertificate = certificate is null;
            _certificate = certificate ?? SelfSignedCertificate.Create("agent-under-test");
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var masters = new TrustStore(new[] { masterCertificate.Thumbprint });
            OwnThread(() =>
            {
                while (true)
                {
                    TcpClient client;
                    try
                    {
                        client = _listener.AcceptTcpClient();
                    }
                    catch (Exception)
                    {
                        return; // Dispose stopped the listener
                    }

                    using (client)
                    {
                        try
                        {
                            using var channel = SecureChannel.AuthenticateAsServer(client, _certificate, masters);
                            var message = channel.Receive();
                            if (message.Type == MessageType.GetWakeAddress)
                            {
                                Interlocked.Increment(ref _questions);
                            }
                            else if (message.Type == MessageType.SetAppBlock
                                     && AppBlockPolicy.TryDecode(message.Payload, out var block))
                            {
                                lock (_blocksReceived)
                                {
                                    _blocksReceived.Add(block.SavedAt);
                                }
                            }
                            else if (message.Type == MessageType.SetPowerSchedule
                                     && PowerSchedule.TryDecode(message.Payload, out var schedule))
                            {
                                lock (_schedulesReceived)
                                {
                                    _schedulesReceived.Add(schedule.SavedAt);
                                }
                            }
                            else if (message.Type == MessageType.SetClassLesson
                                     && ClassLesson.TryDecode(message.Payload, out var lesson))
                            {
                                lock (_lessonsReceived)
                                {
                                    _lessonsReceived.Add(lesson.SavedAt);
                                }
                            }
                            channel.Send(message.Type == MessageType.GetAppBlock && StatusReply is { } status ? status : reply);
                        }
                        catch (Exception)
                        {
                            // The poller gave up on this connection.
                        }
                    }
                }
            });
        }

        public int Port { get; }

        public string Thumbprint => _certificate.Thumbprint;

        // What GetAppBlock is answered with, when it isn't the same as everything else.
        public Message? StatusReply { get; set; }

        public int Questions => Volatile.Read(ref _questions);

        // The SavedAt of each SetAppBlock received, in order.
        public long[] BlocksReceived
        {
            get
            {
                lock (_blocksReceived)
                {
                    return _blocksReceived.ToArray();
                }
            }
        }

        // The SavedAt of each SetPowerSchedule received, in order.
        public long[] SchedulesReceived
        {
            get
            {
                lock (_schedulesReceived)
                {
                    return _schedulesReceived.ToArray();
                }
            }
        }

        // The SavedAt of each SetClassLesson received, in order.
        public long[] LessonsReceived
        {
            get
            {
                lock (_lessonsReceived)
                {
                    return _lessonsReceived.ToArray();
                }
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            if (_ownsCertificate)
            {
                _certificate.Dispose();
            }
        }
    }

    private sealed class FixedSizeCapture : IScreenCapture
    {
        public Bitmap CaptureFrame() => new(1920, 1080);
    }

    private sealed class NoInput : IInputInjector
    {
        public void Inject(InputEvent inputEvent)
        {
        }

        public void TypeText(string text, bool pressEnter)
        {
        }
    }

    private sealed class NoDevices : IInputDeviceController
    {
        public void DisableInputDevices()
        {
        }

        public void EnableInputDevices()
        {
        }
    }
}
