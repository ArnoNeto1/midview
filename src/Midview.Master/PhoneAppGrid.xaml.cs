using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using Midview.Protocol;

namespace Midview.Master;

// The phones' apps as cards with their icons, as in "Bloquear apps nos
// celulares", for "Abrir programa" (one app: SingleChoice) and "Desinstalar
// programa" (several) on phones. Read (off the UI thread) asks every phone at
// once for its list with the apps' flags; Show puts the joined list up.
public partial class PhoneAppGrid : UserControl
{
    private readonly ObservableCollection<Row> _rows = new();

    public PhoneAppGrid()
    {
        InitializeComponent();
        AppsList.ItemsSource = _rows;
    }

    // Ticking one app unticks the others.
    public bool SingleChoice { get; set; }

    // Raised when what is ticked changes.
    public event Action? ChosenChanged;

    public IReadOnlyList<string> Chosen => _rows.Where(row => row.Checked).Select(row => row.Package).ToList();

    public string NameOf(string package) => _rows.FirstOrDefault(row => row.Package == package)?.Name ?? package;

    public int Count => _rows.Count;

    // One phone's list and its decoded icons; null when it didn't answer (or
    // is from before app blocking).
    public sealed record Answer(IReadOnlyList<PhoneApp> Apps, IReadOnlyDictionary<string, ImageSource> Icons);

    public static Task<Answer?[]> Read(IReadOnlyList<KnownMachine> phones, X509Certificate2 masterCertificate) =>
        Task.WhenAll(phones.Select(phone => Task.Run(() => ReadOne(phone, masterCertificate))));

    private static Answer? ReadOne(KnownMachine phone, X509Certificate2 masterCertificate)
    {
        try
        {
            if (PhoneAppBlockClient.ListApps(phone, masterCertificate, withFlags: true) is not { } apps)
            {
                return null;
            }
            var icons = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
            foreach (var app in apps)
            {
                if (PhoneAppBlockWindow.DecodeIcon(app.IconPng) is { } icon)
                {
                    icons[app.Package] = icon;
                }
            }
            return new Answer(apps, icons);
        }
        catch (Exception)
        {
            // Off, or not answering: left out of the list.
            return null;
        }
    }

    // The joined list; what was ticked and is still there stays ticked.
    public void Show(IReadOnlyList<Answer?> answers, bool forUninstall)
    {
        var ticked = Chosen.ToHashSet(StringComparer.Ordinal);
        var icons = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
        foreach (var answer in answers.Where(answer => answer is not null))
        {
            foreach (var (package, icon) in answer!.Icons)
            {
                icons.TryAdd(package, icon);
            }
        }

        _rows.Clear();
        foreach (var pick in PhoneProgramsText.Picks(answers.Select(answer => answer?.Apps).ToList(), forUninstall))
        {
            var row = new Row
            {
                Package = pick.Package,
                Name = pick.Name,
                Detail = pick.Detail,
                CanPick = pick.CanPick,
                Icon = icons.GetValueOrDefault(pick.Package),
                Checked = pick.CanPick && ticked.Contains(pick.Package) && (!SingleChoice || ticked.Count == 1),
            };
            row.PropertyChanged += (_, e) => OnRowChanged(row, e);
            _rows.Add(row);
        }
        ChosenChanged?.Invoke();
    }

    // Ticks the app (a test or a harness picking as the teacher would).
    public void Tick(string package)
    {
        foreach (var row in _rows.Where(row => row.Package == package && row.CanPick))
        {
            row.Checked = true;
        }
    }

    private void OnRowChanged(Row row, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Row.Checked))
        {
            return;
        }
        if (SingleChoice && row.Checked)
        {
            foreach (var other in _rows.Where(other => other != row && other.Checked))
            {
                other.Checked = false;
            }
        }
        ChosenChanged?.Invoke();
    }

    private sealed class Row : INotifyPropertyChanged
    {
        private bool _checked;

        public required string Package { get; init; }

        public required string Name { get; init; }

        public required string Detail { get; init; }

        public required bool CanPick { get; init; }

        public ImageSource? Icon { get; init; }

        public bool HasIcon => Icon is not null;

        // Drawn in place of an icon the phone didn't send.
        public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
