using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Midview.Master;

// Whether the machine answered its last poll, for "Só online": Connecting
// until the first answer or failure.
public enum TilePresence
{
    Connecting,
    Online,
    Offline,
}

// One cell of the classroom grid. Only ever touched on the UI thread.
public sealed class MachineTile : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private string _status = "Conectando...";
    private string _blockBadge = string.Empty;
    private bool _isSelected;
    private string _screenLockBadge = string.Empty;
    private bool _screenLockAlert;
    private TilePresence _presence;
    private TileCell? _cell;

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
    // selects it, it never opens anything by itself. Several can be selected
    // (MainWindow's GridSelection); the two buttons then wait for just one.
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

    // Where the tile sits when the teacher places them like the room
    // (TileGridPanel, TileArrangement); none until it has a place.
    public TileCell? Cell
    {
        get => _cell;
        set
        {
            if (_cell != value)
            {
                _cell = value;
                OnPropertyChanged();
            }
        }
    }

    // What "Só online" goes by (GridFilter); set with every poll's outcome.
    public TilePresence Presence
    {
        get => _presence;
        private set
        {
            if (_presence != value)
            {
                _presence = value;
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
        Presence = TilePresence.Online;
    }

    // The machine answered, but its screen can't be captured right now
    // (e.g. it's locked): still online.
    public void ShowScreenUnavailable()
    {
        Thumbnail = null;
        Status = "Tela indisponível";
        Presence = TilePresence.Online;
    }

    // Why the machine couldn't be polled (FailureText.Status). Clears the
    // last thumbnail: a stale image would suggest the machine still looks
    // like that.
    public void ShowStatus(string status)
    {
        Thumbnail = null;
        Status = status;
        Presence = FailureText.MeansMachineIsOn(status) ? TilePresence.Online : TilePresence.Offline;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
