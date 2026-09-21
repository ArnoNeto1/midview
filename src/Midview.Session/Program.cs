using System.Net;
using System.Net.Sockets;
using Midview.Protocol;
using Midview.Session;

var serverCertificate = SelfSignedCertificate.Create(Environment.MachineName);
Console.WriteLine($"This machine's certificate thumbprint (give this to the teacher's MidviewMaster): {serverCertificate.Thumbprint}");

// TODO (later task, not this one): load the actual trusted master thumbprint(s)
// from a config file instead of accepting a command-line argument.
var masterThumbprint = args.Length > 0 ? args[0] : throw new ArgumentException(
    "Pass the trusted MidviewMaster certificate thumbprint as the first argument for this manual test.");

var server = new SessionServer(
    new TcpListener(IPAddress.Any, 5901),
    serverCertificate,
    new TrustStore(new[] { masterThumbprint }),
    new FramePipeline(new GdiScreenCapture()),
    new SendInputInjector(),
    new InputLockManager(new WindowsInputDeviceController(), new SystemClock(), TimeSpan.FromSeconds(5)));

server.Run(CancellationToken.None);
