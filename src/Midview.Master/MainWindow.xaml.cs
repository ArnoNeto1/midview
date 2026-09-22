using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;

namespace Midview.Master;

// The classroom console: one live thumbnail per saved machine, click one to
// take full control of it (same ViewControlWindow as a manual Connect).
public partial class MainWindow : Window
{
    private readonly X509Certificate2 _masterCertificate = MasterIdentity.LoadOrCreate(MasterIdentity.DefaultCertificatePath);

    // The single source of truth for the roster while the app runs; the
    // file is rewritten from it after every change.
    private readonly ObservableCollection<MachineTileViewModel> _tiles = new();

    public MainWindow()
    {
        InitializeComponent();
        MachinesItemsControl.ItemsSource = _tiles;

        try
        {
            foreach (var machine in KnownMachinesFile.Load(KnownMachinesFile.DefaultPath))
            {
                _tiles.Add(new MachineTileViewModel(machine, _masterCertificate));
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Não foi possível ler a lista da turma: {ex.Message}";
        }

        UpdateEmptyRosterHint();
    }

    private void OnAddMachineClicked(object sender, RoutedEventArgs e) => OpenAddMachineWindow(machine: null);

    private void OnMachineDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTileViewModel tile)
        {
            OpenAddMachineWindow(tile.Machine);
        }
    }

    private void OpenAddMachineWindow(KnownMachine? machine)
    {
        var window = new AddMachineWindow(_masterCertificate, machine) { Owner = this };
        window.MachineSaved += OnMachineSaved;
        window.Show();
    }

    // Same host address as a saved machine means "update that machine"
    // (e.g. its Session was reinstalled and got a new thumbprint), not a
    // second tile for the same computer.
    private void OnMachineSaved(KnownMachine machine)
    {
        var tile = new MachineTileViewModel(machine, _masterCertificate);
        var existing = _tiles.FirstOrDefault(t =>
            string.Equals(t.Machine.HostAddress, machine.HostAddress, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _tiles.Add(tile);
        }
        else
        {
            _tiles[_tiles.IndexOf(existing)] = tile;
            existing.Dispose();
        }

        SaveRoster();
    }

    private void OnRemoveMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTileViewModel tile)
        {
            _tiles.Remove(tile);
            tile.Dispose();
            SaveRoster();
        }
    }

    private void SaveRoster()
    {
        try
        {
            KnownMachinesFile.Save(KnownMachinesFile.DefaultPath, _tiles.Select(t => t.Machine).ToList());
            StatusTextBlock.Text = string.Empty;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Não foi possível salvar a lista da turma: {ex.Message}";
        }

        UpdateEmptyRosterHint();
    }

    private void UpdateEmptyRosterHint() =>
        EmptyRosterTextBlock.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // Exactly what AddMachineWindow's manual Connect builds, from the saved
    // entry instead of re-typed fields. Independent of the tile's own preview
    // connection, which keeps polling alongside full control.
    private async void OnMachineTileClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTileViewModel tile)
        {
            return;
        }

        var machine = tile.Machine;
        StatusTextBlock.Text = $"Conectando a {machine.Name}...";
        try
        {
            var client = await Task.Run(() => ConnectionClient.Connect(
                machine.HostAddress, MidviewPorts.Session, machine.SessionThumbprint, _masterCertificate));

            StatusTextBlock.Text = string.Empty;
            var viewControlWindow = new ViewControlWindow(client);
            viewControlWindow.Show();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Falha ao conectar a {machine.Name}: {ConnectionErrors.Describe(ex)}";
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        foreach (var tile in _tiles)
        {
            tile.Dispose();
        }
    }
}
