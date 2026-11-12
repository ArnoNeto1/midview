using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Midview.Master;

// How one phone stands in the CELULARES windows ("Otimizar", "Atualizar
// celulares"): its mark and color follow from it.
public enum PhoneRowState
{
    Waiting,
    Working,
    Done,
    Partial,
    Failed,
}

// One phone's line in those windows: its name, what is happening there and,
// while its APK is being sent, how far (Progress, 0 to 1; null hides the bar).
public sealed class PhoneRow : INotifyPropertyChanged
{
    private string _detail = string.Empty;
    private PhoneRowState _state;
    private double? _progress;

    public PhoneRow(KnownMachine phone)
    {
        Phone = phone;
    }

    public KnownMachine Phone { get; }

    public string Name => Phone.Name;

    public string Detail
    {
        get => _detail;
        private set => Set(ref _detail, value);
    }

    public PhoneRowState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                Changed(nameof(Mark));
            }
        }
    }

    public string Mark => State switch
    {
        PhoneRowState.Done => "✓",
        PhoneRowState.Partial => "!",
        PhoneRowState.Failed => "✗",
        PhoneRowState.Working => "…",
        _ => "•",
    };

    public double? Progress
    {
        get => _progress;
        set
        {
            if (Set(ref _progress, value))
            {
                Changed(nameof(HasProgress));
                Changed(nameof(ProgressScale));
            }
        }
    }

    public bool HasProgress => _progress is not null;

    public double ProgressScale => Math.Clamp(_progress ?? 0, 0, 1);

    public void Show(PhoneRowState state, string detail)
    {
        State = state;
        Detail = detail;
        if (state != PhoneRowState.Working)
        {
            Progress = null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        Changed(name!);
        return true;
    }
}
