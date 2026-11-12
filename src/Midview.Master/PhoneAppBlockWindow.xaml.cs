using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Midview.Protocol;

namespace Midview.Master;

// Blocks apps on one phone or the class's phones (see PhoneAppPolicy): the
// teacher ticks apps in the list the phones send (icon and name) and picks a
// mode; "Liberar tudo" lifts it. The phone keeps the block and enforces it.
// The PCs' programs have their own window (AppBlockWindow).
public partial class PhoneAppBlockWindow : MidviewWindow
{
    private readonly IReadOnlyList<KnownMachine> _phones;
    private readonly X509Certificate2 _masterCertificate;
    private readonly Func<PhoneAppPolicy?> _classPolicy;
    private readonly Action<PhoneAppPolicy>? _onClassPolicyApplied;
    private readonly ObservableCollection<AppRow> _rows = new();
    private IReadOnlyList<PhoneBlockState> _states = Array.Empty<PhoneBlockState>();
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private bool _preselected;
    private bool _loading;
    private bool _applying;

    // allPhones: phones is every phone in the classroom list; only then is
    // onClassPolicyApplied set, so the Master remembers the block and resends
    // it to phones that were off. classPolicy: the block it remembers now, which
    // a new one must be newer than.
    public PhoneAppBlockWindow(
        IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate,
        Func<PhoneAppPolicy?> classPolicy, Action<PhoneAppPolicy>? onClassPolicyApplied = null)
    {
        InitializeComponent();
        _phones = phones;
        _masterCertificate = masterCertificate;
        _classPolicy = classPolicy;
        _onClassPolicyApplied = onClassPolicyApplied;
        TargetsTextBlock.Text = PhoneAppBlockText.Targets(phones.Select(phone => phone.Name).ToList(), allPhones);
        AppsList.ItemsSource = _rows;
        Loaded += async (_, _) => await LoadAsync();
    }

    private sealed class AppRow : INotifyPropertyChanged
    {
        private bool _checked;
        private string _detail = string.Empty;

        public required string Package { get; init; }

        public required string Name { get; init; }

        public ImageSource? Icon { get; init; }

        public bool HasIcon => Icon is not null;

        // Drawn in place of an icon the phone didn't send.
        public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";

        public string Detail
        {
            get => _detail;
            set => Set(ref _detail, value);
        }

        public bool Checked
        {
            get => _checked;
            set => Set(ref _checked, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    // The state first, then the apps: the teacher can already lift while the
    // lists load (a phone that is off makes them slow).
    private async Task LoadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        UpdateButtons();
        StateTextBlock.Text = "Lendo o estado atual...";
        ListTextBlock.Text = "Pedindo a lista de apps...";
        try
        {
            _states = await ReadStatesAsync();
            StateTextBlock.Text = PhoneAppBlockText.Summarize(_states, NameOf);
            var preselect = !_preselected ? PhoneAppBlockText.CommonActivePolicy(_states) : null;
            if (!_preselected)
            {
                _preselected = true;
                if (preselect is not null)
                {
                    (preselect.Mode == AppBlockMode.AllowOnly ? AllowOnlyRadioButton : BlockListRadioButton).IsChecked = true;
                }
            }

            // Only a phone that knows app blocking is asked for its apps.
            var asked = _phones.Where((_, index) => _states[index].Status is { OldAgent: false }).ToList();
            var answers = await Task.WhenAll(asked.Select(phone => Task.Run(() => ReadApps(phone))));
            var answered = answers.Count(answer => answer is not null);
            ShowApps(PhoneAppBlockText.Union(answers.Select(answer => answer?.Apps)), answers, answered, preselect);
            ListTextBlock.Text = PhoneAppBlockText.ListLine(answered, _phones.Count);
            StateTextBlock.Text = PhoneAppBlockText.Summarize(_states, NameOf);
        }
        finally
        {
            _loading = false;
            UpdateButtons();
        }
    }

    private string NameOf(string package) => _names.TryGetValue(package, out var name) ? name : package;

    // One answer per phone, in the phones' order, all asked at once: a phone
    // that is off costs its connection timeout once.
    private Task<PhoneBlockState[]> ReadStatesAsync() =>
        Task.WhenAll(_phones.Select(phone => Task.Run(() => ReadState(phone))));

    private PhoneBlockState ReadState(KnownMachine phone)
    {
        try
        {
            return new PhoneBlockState(phone.Name, PhoneAppBlockClient.GetStatus(phone, _masterCertificate));
        }
        catch (Exception)
        {
            return new PhoneBlockState(phone.Name, null);
        }
    }

    // The phone's apps and their icons, decoded here (off the UI thread,
    // frozen); null when it didn't answer.
    private sealed record PhoneAnswer(IReadOnlyList<PhoneApp> Apps, IReadOnlyDictionary<string, ImageSource> Icons);

    private PhoneAnswer? ReadApps(KnownMachine phone)
    {
        try
        {
            if (PhoneAppBlockClient.ListApps(phone, _masterCertificate) is not { } apps)
            {
                return null;
            }
            var icons = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
            foreach (var app in apps)
            {
                if (DecodeIcon(app.IconPng) is { } icon)
                {
                    icons[app.Package] = icon;
                }
            }
            return new PhoneAnswer(apps, icons);
        }
        catch (Exception)
        {
            // Off, or not answering: left out of the list.
            return null;
        }
    }

    // The PNG comes from the student's phone: anything that doesn't decode
    // is simply drawn without an icon.
    private static ImageSource? DecodeIcon(byte[] png)
    {
        if (png.Length == 0)
        {
            return null;
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 48;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException
                                       or IOException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    // A new list keeps what the teacher ticked; an app ticked (or already
    // blocked) that no phone listed stays, named by its package.
    private void ShowApps(IReadOnlyList<PhoneAppRow> union, PhoneAnswer?[] answers, int answered, PhoneAppPolicy? preselect)
    {
        var ticked = _rows.Where(row => row.Checked).Select(row => row.Package)
            .Concat(preselect?.Packages ?? Array.Empty<string>())
            .ToHashSet(StringComparer.Ordinal);

        _rows.Clear();
        foreach (var app in union)
        {
            _names[app.App.Package] = app.Name;
            var icon = answers.Select(answer => answer?.Icons.GetValueOrDefault(app.App.Package)).FirstOrDefault(found => found is not null);
            _rows.Add(new AppRow
            {
                Package = app.App.Package,
                Name = app.Name,
                Icon = icon,
                Detail = PhoneAppBlockText.RowDetail(app.Phones, answered),
                Checked = ticked.Remove(app.App.Package),
            });
        }
        foreach (var package in ticked.OrderBy(package => package, StringComparer.Ordinal))
        {
            _rows.Add(new AppRow
            {
                Package = package,
                Name = NameOf(package),
                Detail = PhoneAppBlockText.RowDetail(0, answered),
                Checked = true,
            });
        }
        UpdateChosen();
        foreach (var row in _rows)
        {
            row.PropertyChanged += (_, _) => UpdateChosen();
        }
    }

    private void UpdateChosen()
    {
        var chosen = _rows.Count(row => row.Checked);
        ChosenTextBlock.Text = chosen switch
        {
            0 => string.Empty,
            1 => "1 marcado",
            _ => $"{chosen} marcados",
        };
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private async void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        var mode = AllowOnlyRadioButton.IsChecked == true ? AppBlockMode.AllowOnly : AppBlockMode.BlockList;
        var packages = _rows.Where(row => row.Checked).Select(row => row.Package).ToList();
        if (mode == AppBlockMode.BlockList && packages.Count == 0)
        {
            ProgressTextBlock.Text = "Marque pelo menos um app, ou use \"Liberar tudo\".";
            return;
        }
        if (packages.Count > PhoneAppPolicy.MaxPackages)
        {
            ProgressTextBlock.Text = $"Marque no máximo {PhoneAppPolicy.MaxPackages} apps.";
            return;
        }

        await ApplyAsync(new PhoneAppPolicy(NextSavedAt(), mode, packages));
    }

    private async void OnLiftClicked(object sender, RoutedEventArgs e) =>
        await ApplyAsync(PhoneAppPolicy.Lifted(NextSavedAt()));

    private long NextSavedAt() =>
        PhoneAppBlockText.NextSavedAt(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), _states, _classPolicy());

    private async Task ApplyAsync(PhoneAppPolicy policy)
    {
        if (_applying)
        {
            return;
        }

        _applying = true;
        UpdateButtons();
        ProgressTextBlock.Text = "Aplicando...";
        ResultTextBlock.Text = string.Empty;

        try
        {
            var results = await Task.WhenAll(_phones.Select(phone => Task.Run(() => ApplyOn(phone, policy))));
            var applied = results.Count(result => result.Applied);
            ProgressTextBlock.Text = applied == results.Length
                ? results.Length == 1 ? "Concluído." : $"Concluído nos {results.Length} celulares."
                : $"Concluído em {applied} de {results.Length}.";
            var lines = results.Select(result => $"{(result.Applied ? "✓" : "✗")} {result.Name}: {result.Detail}").ToList();
            var keptNewer = results.Any(result => result.KeptNewer);
            if (keptNewer)
            {
                lines.Add("Algum celular já tinha um bloqueio mais novo (de outro professor, ou salvo com o relógio adiantado) e o seu não valeu nele. "
                          + "Veja o estado atual acima e aplique de novo se quiser trocar.");
            }
            ResultTextBlock.Text = string.Join("\n", lines);

            // Only the block of every phone is remembered and resent to phones
            // that were off; one some phone turned down for a newer one isn't it.
            if (!keptNewer)
            {
                _onClassPolicyApplied?.Invoke(policy);
            }
        }
        finally
        {
            _applying = false;
            UpdateButtons();
        }

        _states = await ReadStatesAsync();
        StateTextBlock.Text = PhoneAppBlockText.Summarize(_states, NameOf);
    }

    private (string Name, bool Applied, bool KeptNewer, string Detail) ApplyOn(KnownMachine phone, PhoneAppPolicy policy)
    {
        if (phone.AgentThumbprint is not { Length: > 0 })
        {
            return (phone.Name, false, false, "falta a identidade do celular: pareie-o de novo em \"Máquinas da sala\"");
        }
        try
        {
            var (succeeded, detail) = PhoneAppBlockClient.Apply(phone, _masterCertificate, policy);
            var keptNewer = PhoneAppBlockText.KeptNewer(succeeded, detail);
            return (phone.Name, succeeded && !keptNewer, keptNewer, PhoneAppBlockText.ApplyLine(succeeded, detail));
        }
        catch (Exception ex)
        {
            return (phone.Name, false, false, FailureText.DescribeAgent(ex));
        }
    }

    private void UpdateButtons()
    {
        ApplyButton.IsEnabled = LiftButton.IsEnabled = !_applying;
        RefreshButton.IsEnabled = !_applying && !_loading;
    }
}
