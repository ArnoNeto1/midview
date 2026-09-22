using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Media.Imaging;

namespace Midview.Master;

// Keeps one machine's grid tile fresh: every interval, asks its Session for
// a thumbnail over a long-lived PreviewClient. Each machine gets its own
// poller on its own thread, so a slow or offline machine never delays
// another machine's tile.
public sealed class MachinePreviewPoller : IDisposable
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(5);

    private readonly KnownMachine _machine;
    private readonly int _port;
    private readonly X509Certificate2 _masterCertificate;
    private readonly TimeSpan _interval;
    private readonly Action<BitmapSource?> _onResult;
    private readonly ManualResetEventSlim _stopRequested = new();
    private readonly Thread _thread;

    // onResult runs on the poller's own thread, once per cycle: a frozen
    // thumbnail, or null when that cycle failed for any reason.
    public MachinePreviewPoller(
        KnownMachine machine, int port, X509Certificate2 masterCertificate, TimeSpan interval,
        Action<BitmapSource?> onResult)
    {
        _machine = machine;
        _port = port;
        _masterCertificate = masterCertificate;
        _interval = interval;
        _onResult = onResult;
        _thread = new Thread(PollLoop) { IsBackground = true, Name = $"Preview poller: {machine.Name}" };
    }

    public void Start() => _thread.Start();

    // Doesn't wait for the thread: it notices within one network timeout and
    // exits on its own, which beats blocking the UI thread that long.
    public void Dispose() => _stopRequested.Set();

    private void PollLoop()
    {
        PreviewClient? client = null;
        try
        {
            while (!_stopRequested.IsSet)
            {
                BitmapSource? thumbnail;
                try
                {
                    client ??= PreviewClient.Connect(
                        _machine.HostAddress, _port, _machine.SessionThumbprint, _masterCertificate, NetworkTimeout);
                    thumbnail = Decode(client.RequestThumbnail());
                }
                catch (Exception)
                {
                    // Offline, unreachable, wrong thumbprint, no reply in
                    // time, a corrupt image: all the same to the grid. Show
                    // the placeholder, and start over on a fresh connection
                    // next cycle -- an SslStream that failed mid-read can't
                    // be reused.
                    client?.Dispose();
                    client = null;
                    thumbnail = null;
                }

                if (_stopRequested.IsSet)
                {
                    break;
                }
                _onResult(thumbnail);
                _stopRequested.Wait(_interval);
            }
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static BitmapSource Decode(byte[] jpegBytes)
    {
        using var stream = new MemoryStream(jpegBytes);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze(); // decoded on this thread; freezing is what lets the UI thread use it
        return frame;
    }
}
