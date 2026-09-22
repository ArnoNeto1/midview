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

var inputDeviceController = new WindowsInputDeviceController();

// A previous run's lock only clears via StopControl or the heartbeat
// timeout -- neither can fire if the process (or machine) was killed
// abruptly (a crash, a reboot mid-session) before either had a chance to
// run. That leaves input devices disabled at the driver level while a
// freshly-started process's own InputLockManager believes, incorrectly,
// that nothing is locked. Unconditionally re-enabling on every startup
// guarantees a known-good state regardless of how the last run ended.
inputDeviceController.EnableInputDevices();

var server = new SessionServer(
    new TcpListener(IPAddress.Any, 5901),
    serverCertificate,
    new TrustStore(new[] { masterThumbprint }),
    new GdiScreenCapture(),
    new SendInputInjector(),
    new InputLockManager(inputDeviceController, new SystemClock(), TimeSpan.FromSeconds(5)));

server.Run(CancellationToken.None);
