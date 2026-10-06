using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Midview.Master;

// One machine in "Máquinas da sala" after a search, and how pairing it
// goes. Session is the open pairing while the teacher compares the codes.
public sealed class FoundRow : INotifyPropertyChanged
{
    private string _status;
    private bool _isChecked;
    private string _codeText = string.Empty;
    private bool _awaitingConfirmation;
    private string _outcome = string.Empty;
    private bool _outcomeIsError;

    public FoundRow(FoundMachine machine, string status, bool isChecked)
    {
        Machine = machine;
        _status = status;
        _isChecked = isChecked;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FoundMachine Machine { get; }

    public string Name => Machine.Name;

    public string Address => Machine.Address;

    public PairingSession? Session { get; set; }

    // Being paired: left out of another "Parear".
    public bool IsBusy { get; set; }

    public string Status { get => _status; set => Set(ref _status, value); }

    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    public string CodeText { get => _codeText; set => Set(ref _codeText, value); }

    public bool AwaitingConfirmation { get => _awaitingConfirmation; set => Set(ref _awaitingConfirmation, value); }

    public string Outcome { get => _outcome; set => Set(ref _outcome, value); }

    public bool OutcomeIsError { get => _outcomeIsError; set => Set(ref _outcomeIsError, value); }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
