using System.Net;
using System.Net.Sockets;
using Midview.Protocol;
using Midview.Session;

var serverCertificate = SelfSignedCertificate.Create(Environment.MachineName);
Console.WriteLine($"This machine's certificate thumbprint (give this to the teacher's MidviewMaster): {serverCertificate.Thumbprint}");

// Reuses the same trusted-masters.json the Agent already reads for
// site-blocking commands -- one registration per machine covers both, so
// the teacher never has to pass a thumbprint by hand again after the first
// setup. A command-line argument is still accepted for one-off manual
// testing without touching that shared file.
var trustedMastersPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "Midview", "trusted-masters.json");
var trustedMasters = args.Length > 0 ? new[] { args[0] } : TrustedMastersFile.Load(trustedMastersPath);
if (trustedMasters.Count == 0)
{
    throw new InvalidOperationException(
        $"No trusted MidviewMaster found. Add its certificate thumbprint to {trustedMastersPath} " +
        "(the same file the Agent uses), e.g. [\"<thumbprint>\"], or pass it as a command-line argument.");
}

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
    new TrustStore(trustedMasters),
    new GdiScreenCapture(),
    new SendInputInjector(),
    new InputLockManager(inputDeviceController, new SystemClock(), TimeSpan.FromSeconds(5)));

server.Run(CancellationToken.None);
