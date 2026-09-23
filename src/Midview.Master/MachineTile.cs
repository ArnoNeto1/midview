using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Midview.Master;

// One cell of the classroom grid. Only ever touched on the UI thread.
public sealed class MachineTile : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private string _status = "Conectando...";

    public MachineTile(KnownMachine machine)
    {
        Machine = machine;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public KnownMachine Machine { get; }

    public string Name => Machine.Name;

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
