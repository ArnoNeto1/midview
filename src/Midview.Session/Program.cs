using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;
using Midview.Session;

// Created by the Agent on its first start (Session, as a standard user,
// can't write there itself), so the thumbprint the teacher registered stays
// the same across restarts and reboots. The Agent also logs it to
// %ProgramData%\Midview\agent.log.
var identityPath = SessionIdentity.DefaultCertificatePath;
X509Certificate2 serverCertificate;
if (File.Exists(identityPath))
{
    serverCertificate = SessionIdentity.Load(identityPath);
}
else
{
    Console.WriteLine($"No {identityPath} yet (the Agent creates it on its first start) -- using a temporary certificate whose thumbprint changes on every launch.");
    serverCertificate = SelfSignedCertificate.Create(Environment.MachineName);
}
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

// The input lock itself is applied by the Agent, which re-enables the
// devices whenever this process's connection to it closes and whenever it
// starts at boot -- a crash, kill or power cut mid-lock can't leave them
// disabled, and Session has nothing to clean up at startup.
using var inputDeviceController = new AgentInputDeviceController();

var server = new SessionServer(
    new TcpListener(IPAddress.Any, 5901),
    serverCertificate,
    new TrustStore(trustedMasters),
    new GdiScreenCapture(),
    new SendInputInjector(),
    new InputLockManager(inputDeviceController, new SystemClock(), TimeSpan.FromSeconds(5)));

server.Run(CancellationToken.None);
