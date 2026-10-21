using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;
using Midview.Session;

// Without DPI awareness, Windows scales this process's view of the screen on
// a display set to 125%/150%: screen bounds come back smaller than the real
// resolution and the capture only covers part of the screen. Must run before
// anything touches the screen.
System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
System.Windows.Forms.Application.EnableVisualStyles(); // for the teacher-message and chat windows

// Started by the Agent to show a pairing code (see PairingCodeWindow): only
// the window, never a second server.
if (PairingCodeArguments.Parse(args) is { } pairing)
{
    PairingCodeWindow.Run(pairing.Code, pairing.TeacherName);
    return;
}

// Started by the Agent while a shutdown or restart is on its way: the
// countdown banner only. Its last words stay up until Windows shuts down
// (or the Agent ends it, when the teacher cancels).
if (CountdownArguments.Parse(args) is { } countdown)
{
    var (running, done) = CountdownText.Power(countdown.Kind);
    CountdownBanner.Run(running, done, TimeSpan.FromSeconds(countdown.Seconds), linger: TimeSpan.FromMinutes(10));
    return;
}

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

// trusted-masters.json: the teachers this machine trusts, written by the
// Agent when a teacher pairs (or when a trusted teacher gives a colleague
// access, TrustTeacher) and read again whenever it changes, so a teacher who
// pairs is accepted on their next connection. With no teacher
// yet, Session still runs and refuses everyone: pairing goes through the
// Agent. A thumbprint on the command line is still accepted for one-off
// manual testing.
var trustedMastersPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "Midview", "trusted-masters.json");
var masterTrustStore = args.Length > 0 ? new TrustStore(new[] { args[0] }) : TrustStore.FromFile(trustedMastersPath);
if (masterTrustStore.Count == 0)
{
    Console.WriteLine($"No teacher has paired with this machine yet ({trustedMastersPath}): refusing every connection until one does.");
}

// The student's "Entregar ao professor" folder, created at login so it's
// there to put work into before the teacher collects it.
var deliveryFolder = new DeliveryFolder(DeliveryFolder.DefaultDirectory);
deliveryFolder.TryCreate();

// The class folder ("Pasta da turma"): the lesson's folder on the student's
// Desktop, and where a finished lesson's files wait for the teacher's Master.
// The Agent says which lesson is on (class-lesson.json); Follow, every few
// seconds, tells the student about a new lesson and sets the old one's files
// aside. The folders are the student's own, so all of it happens here.
var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var classFolder = new ClassFolder(
    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
    Path.Combine(localAppData, "Midview", "Pasta da turma guardada"),
    () => ClassLessonFile.Read(ClassLessonFile.DefaultPath),
    new WindowsRecycleBin(),
    Path.Combine(localAppData, "Midview", "class-folder-session.json"));
var messageDisplay = new WinFormsMessageDisplay();

// A new lesson's notice goes in a corner of the screen and away on its own,
// never taking the focus (not the teacher's message window, which waits for
// "OK, ENTENDI"). `using`, so the timer is still referred to (and not
// collected) for as long as the server runs below.
var lessonNotice = new WinFormsCornerNotice();
using var classFolderTimer = new System.Threading.Timer(
    _ => classFolder.Follow(lessonNotice), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

var server = new SessionServer(
    new TcpListener(IPAddress.Any, 5901),
    serverCertificate,
    masterTrustStore,
    new GdiScreenCapture(),
    new SendInputInjector(),
    // The keyboard and mouse are locked by swallowing physical input in this
    // process, never by disabling the devices: on the lab's PS/2 machines
    // Windows applies some of those changes only after a restart, which
    // left a mouse disabled until the machine was restarted. Whatever ends
    // this process -- a crash, a kill, logging off -- ends the lock with it.
    new InputLockManager(new LowLevelInputBlocker(), new SystemClock(), TimeSpan.FromSeconds(5), new WinFormsControlNotice()),
    messageDisplay,
    new WinFormsChatWindows(),
    new FileInbox(FileInbox.DefaultDirectory),
    new WindowsAppCloser(),
    deliveryFolder,
    new WindowsOpenPrograms(),
    new WinFormsScreenLock(),
    new WindowsFileOpener(),
    new WindowsRecycleBin(),
    new WinFormsCountdownNotice(),
    new WindowsWebsiteOpener(),
    new WindowsProgramStarter(),
    clipboard: new WindowsClipboard(),
    classFolder: classFolder);

server.Run(CancellationToken.None);
