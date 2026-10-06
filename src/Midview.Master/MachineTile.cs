using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Midview.Master;

// One cell of the classroom grid. Only ever touched on the UI thread.
public sealed class MachineTile : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private string _status = "Conectando...";
    private string _blockBadge = string.Empty;
    private bool _isSelected;
    private string _screenLockBadge = string.Empty;
    private bool _screenLockAlert;

    public MachineTile(KnownMachine machine)
    {
        Machine = machine;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public KnownMachine Machine { get; private set; }

    public string Name => Machine.Name;

    // Nothing on the tile shows it, and nothing the poller uses changes.
    public void RememberMacAddress(string macAddress) => Machine = Machine with { MacAddress = macAddress };

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    // Shown over the thumbnail area; empty while a live thumbnail is showing.
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    // A short line under the name when programs are blocked on the machine
    // ("🔒 3 bloqueados"); empty when they aren't.
    public string BlockBadge
    {
        get => _blockBadge;
        set
        {
            if (_blockBadge != value)
            {
                _blockBadge = value;
                OnPropertyChanged();
            }
        }
    }

    // The machine "Ver a tela" and "Controlar" act on: a click on a tile
    // selects it, it never opens anything by itself.
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    // A line under the name while the machine is meant to be locked
    // (ScreenLockText); empty otherwise. Alert marks one that isn't.
    public string ScreenLockBadge
    {
        get => _screenLockBadge;
        private set
        {
            if (_screenLockBadge != value)
            {
                _screenLockBadge = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ScreenLockAlert
    {
        get => _screenLockAlert;
        private set
        {
            if (_screenLockAlert != value)
            {
                _screenLockAlert = value;
                OnPropertyChanged();
            }
        }
    }

    public void ShowScreenLock(string text, bool alert)
    {
        ScreenLockBadge = text;
        ScreenLockAlert = alert;
    }

    public void ShowThumbnail(ImageSource thumbnail)
    {
        Thumbnail = thumbnail;
        Status = string.Empty;
    }

    // Clears the last thumbnail: a stale image would suggest the machine
    // still looks like that.
    public void ShowStatus(string status)
    {
        Thumbnail = null;
        Status = status;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
