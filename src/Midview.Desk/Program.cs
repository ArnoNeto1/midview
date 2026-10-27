using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Midview.Desk;
using Midview.Protocol;
using Midview.Session;

// Without DPI awareness, Windows scales this process's view of the screen on
// a display set to 125%/150%: screen bounds come back smaller than the real
// resolution and the capture only covers part of the screen. Must run before
// anything touches the screen. (Here, not in DeskScreenCapture: it is a
// one-time per-process setting, and this helper owns no windows -- it only sets
// DPI awareness, never EnableVisualStyles.)
System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);

// The port the Master's control-via-UAC connection reaches this helper on.
const int Port = 5902; // must match AgentServerWorker.DeskPort (added in Task 9)

// The Agent's own identity, created and owned by the Agent (Session, a standard
// user, can't write there). Must match AgentIdentity.DefaultCertificatePath.
var agentIdentityPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "Midview", "agent-identity.pfx");
if (!File.Exists(agentIdentityPath))
{
    // The Agent creates the identity before it launches this helper; without it
    // there is nothing to prove this machine with. Fail closed.
    Console.Error.WriteLine(
        $"No Agent identity at {agentIdentityPath} -- the Agent creates it before starting this helper. Exiting.");
    return 1;
}

// Load read-only: MachineKeySet (the private key lives in the machine store,
// ACL-restricted to administrators and SYSTEM, which this helper runs as), and
// NOT Exportable -- the helper only uses the key to authenticate, never rewrites
// or exports it. Creating the identity is AgentIdentity.LoadOrCreate's job.
var agentCertificate = new X509Certificate2(
    agentIdentityPath, (string?)null, X509KeyStorageFlags.MachineKeySet);

// The teachers this machine trusts: the same trusted-masters.json the Agent and
// Session read, re-read on change so a teacher who pairs is accepted without a
// restart. Must match AgentServerWorker.TrustedMastersPath.
var trustedMastersPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "Midview", "trusted-masters.json");
var masterTrustStore = TrustStore.FromFile(trustedMastersPath);

// ExclusiveAddressUse so nothing else can share-bind the port underneath us.
var listener = new TcpListener(IPAddress.Any, Port) { ExclusiveAddressUse = true };

// Follows the desktop that currently takes the keyboard and mouse. OpenInputDesktop
// hands back a new handle every call, so InputDesktopSource keys on the desktop
// name to give the follower a stable handle and holds a superseded handle until
// the worker thread has re-attached off it.
var inputDesktops = new InputDesktopSource(
    () => DeskNativeMethods.OpenInputDesktop(0, false, DeskNativeMethods.GenericAll),
    DeskNativeMethods.DesktopName,
    handle => DeskNativeMethods.CloseDesktop(handle));

// Runs on DeskServer's worker thread, which owns no window or hook, so
// SetThreadDesktop succeeds. Once it has attached to the new desktop, the handle
// the thread left behind is finally safe to close (ReleaseSuperseded); never
// before, or CloseDesktop would be closing the desktop the thread is still on.
void AttachThreadToDesktop(IntPtr desktop)
{
    if (DeskNativeMethods.SetThreadDesktop(desktop))
    {
        inputDesktops.ReleaseSuperseded();
    }
    else
    {
        Console.Error.WriteLine(
            $"SetThreadDesktop failed (Win32 {Marshal.GetLastWin32Error()}); keeping the old desktop handle until the next successful attach.");
    }
}

// CancellationToken.None: nothing stops this helper but its process ending -- the
// Agent's supervisor starts it and kills it -- the same as SessionServer's Run
// in Session/Program.cs.
new DeskServer(
    listener,
    agentCertificate,
    masterTrustStore,
    () => new DeskScreenCapture(),
    new SendInputInjector(),
    new DesktopFollower(inputDesktops.Current),
    AttachThreadToDesktop).Run(CancellationToken.None);

return 0;

// The Win32 the input-desktop following needs, wired into InputDesktopSource and
// the attach seam above. All user32 desktop calls; verified live (Task 12).
internal static class DeskNativeMethods
{
    // Full access to the opened input desktop -- enough to attach a thread to it
    // with SetThreadDesktop.
    public const uint GenericAll = 0x10000000; // GENERIC_ALL

    private const int UoiName = 2; // UOI_NAME

    // Opens the desktop currently receiving user input (Default, or the Winlogon
    // secure desktop while a UAC prompt is up). A fresh handle each call;
    // IntPtr.Zero on failure.
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    // Attaches the calling thread to a desktop; fails on a thread that owns a
    // window or hook, which the Desk worker thread never does.
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetThreadDesktop(IntPtr hDesktop);

    // Fails while a thread in this process still has the handle as its thread
    // desktop -- hence the hold-until-attached lifetime in InputDesktopSource.
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(
        IntPtr hObj, int nIndex, byte[] pvInfo, int nLength, out int lpnLengthNeeded);

    // The desktop's name ("Default", "Winlogon", ...), or null if it can't be
    // read. Desktop names are short, so one fixed buffer is enough; the stored
    // string is Unicode and null-terminated inside it.
    public static string? DesktopName(IntPtr handle)
    {
        var buffer = new byte[256];
        if (!GetUserObjectInformation(handle, UoiName, buffer, buffer.Length, out _))
        {
            return null;
        }
        return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }
}
