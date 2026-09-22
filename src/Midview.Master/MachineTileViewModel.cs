using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Midview.Master;

// One tile in the classroom grid. Owns that machine's preview poller and
// moves each poll result onto the UI thread.
public sealed class MachineTileViewModel : INotifyPropertyChanged, IDisposable
{
    // Frequent enough to feel live, far below full control's ~15 fps.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly MachinePreviewPoller _poller;

    public MachineTileViewModel(KnownMachine machine, X509Certificate2 masterCertificate)
    {
        Machine = machine;
        _poller = new MachinePreviewPoller(machine, MidviewPorts.Session, masterCertificate, PollInterval, OnPollResult);
        _poller.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public KnownMachine Machine { get; }

    public string Name => Machine.Name;

    public ImageSource? Thumbnail { get; private set; }

    public string StatusText { get; private set; } = "Conectando...";

    // Shown both before the first image and whenever the machine is
    // unreachable -- the tile never keeps a stale frame on screen.
    public bool ShowPlaceholder => Thumbnail is null;

    public void Dispose() => _poller.Dispose();

    private void OnPollResult(BitmapSource? thumbnail) => _dispatcher.InvokeAsync(() =>
    {
        Thumbnail = thumbnail;
        StatusText = thumbnail is null ? "Sem conexão" : string.Empty;
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowPlaceholder));
    });

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
