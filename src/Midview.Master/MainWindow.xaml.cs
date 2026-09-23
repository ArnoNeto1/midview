using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Midview.Master;

// The classroom console: one live thumbnail per machine in the teacher's
// classroom list; clicking one opens full control on it. Testing and
// saving machines, one-off connections and site-blocking commands live in
// AddMachineWindow.
public partial class MainWindow : Window
{
    private readonly X509Certificate2 _masterCertificate = MasterIdentity.LoadOrCreate(MasterIdentity.DefaultCertificatePath);
    private readonly ObservableCollection<MachineTile> _tiles = new();
    private readonly Dictionary<MachineTile, ThumbnailPoller> _pollers = new();

    public MainWindow()
    {
        InitializeComponent();
        MachinesItemsControl.ItemsSource = _tiles;

        foreach (var machine in LoadRoster())
        {
            InsertTile(_tiles.Count, machine);
        }
        UpdateEmptyHint();
    }

    private async void OnMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTile tile)
        {
            return;
        }

        var machine = tile.Machine;
        StatusTextBlock.Text = $"Conectando a {machine.Name}...";
        try
        {
            var client = await Task.Run(() => ConnectionClient.Connect(
                machine.HostAddress, ConnectionClient.SessionPort, machine.SessionThumbprint, _masterCertificate));

            StatusTextBlock.Text = string.Empty;
            new ViewControlWindow(client) { Title = $"MidviewMaster - {machine.Name}" }.Show();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Não foi possível conectar a {machine.Name}: {FailureText.Describe(ex)}";
        }
    }

    private void OnAddMachineClicked(object sender, RoutedEventArgs e) => OpenAddMachineWindow(existing: null);

    private void OnMessageClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para receber a mensagem.";
            return;
        }
        OpenSendMessageWindow(_tiles.Select(tile => tile.Machine).ToList());
    }

    private void OnMessageMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenSendMessageWindow(new[] { tile.Machine });
        }
    }

    private void OpenSendMessageWindow(IReadOnlyList<KnownMachine> targets) =>
        new SendMessageWindow(targets, _masterCertificate) { Owner = this }.Show();

    private void OnEditMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenAddMachineWindow(tile.Machine);
        }
    }

    private void OpenAddMachineWindow(KnownMachine? existing) =>
        new AddMachineWindow(_masterCertificate, SaveMachine, existing) { Owner = this }.Show();

    private void OnRemoveMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTile tile)
        {
            return;
        }

        var answer = MessageBox.Show(this, $"Remover \"{tile.Name}\" da lista da sala?", "Midview",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        // Gone already if a save from an AddMachineWindow replaced it while
        // the confirmation was open.
        var index = _tiles.IndexOf(tile);
        if (index < 0)
        {
            return;
        }

        RemoveTileAt(index);
        UpdateEmptyHint();
        try
        {
            SaveRoster();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = $"Removida da grade, mas a lista não pôde ser gravada em disco: {ex.Message}";
        }
    }

    // Called by AddMachineWindow. The same name means the same machine:
    // saving "Aluno-3" again replaces it (in place) instead of adding a
    // second tile. The grid is updated first; a failure to write the file
    // then propagates for AddMachineWindow to report.
    private void SaveMachine(KnownMachine? original, KnownMachine updated)
    {
        var position = _tiles.Count;
        for (var i = _tiles.Count - 1; i >= 0; i--)
        {
            var machine = _tiles[i].Machine;
            if (machine == original || string.Equals(machine.Name, updated.Name, StringComparison.OrdinalIgnoreCase))
            {
                RemoveTileAt(i);
                position = i;
            }
        }

        InsertTile(position, updated);
        UpdateEmptyHint();
        SaveRoster();
    }

    // Never keeps Master from starting: at worst the grid starts empty and
    // the status bar says why.
    private List<KnownMachine> LoadRoster()
    {
        try
        {
            var machines = KnownMachinesFile.Load(KnownMachinesFile.DefaultPath, out var corruptCopyPath);
            if (corruptCopyPath is not null)
            {
                StatusTextBlock.Text =
                    $"A lista da sala não pôde ser lida e foi guardada em {corruptCopyPath}; começando com a lista vazia.";
            }
            return machines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = $"A lista da sala não pôde ser lida: {ex.Message}";
            return new List<KnownMachine>();
        }
    }

    private void SaveRoster() =>
        KnownMachinesFile.Save(KnownMachinesFile.DefaultPath, _tiles.Select(tile => tile.Machine));

    private void InsertTile(int index, KnownMachine machine)
    {
        var tile = new MachineTile(machine);
        _tiles.Insert(index, tile);

        var poller = new ThumbnailPoller(machine, _masterCertificate);
        // Decoded on the poller's own thread (frozen, so the UI thread may
        // use it); only the swap onto the tile happens on the UI thread.
        poller.ThumbnailReceived += jpeg =>
        {
            var image = DecodeThumbnail(jpeg);
            Dispatcher.InvokeAsync(() =>
            {
                if (image is null)
                {
                    tile.ShowStatus("Tela indisponível");
                }
                else
                {
                    tile.ShowThumbnail(image);
                }
            });
        };
        poller.Unreachable += reason => Dispatcher.InvokeAsync(() => tile.ShowStatus(reason));
        _pollers.Add(tile, poller);
        poller.Start();
    }

    private void RemoveTileAt(int index)
    {
        var tile = _tiles[index];
        _tiles.RemoveAt(index);
        if (_pollers.Remove(tile, out var poller))
        {
            poller.Dispose();
        }
    }

    // An empty thumbnail means the machine answered but couldn't capture
    // its screen right now (e.g. it's locked); a corrupt one is shown the
    // same way rather than failing the whole poll.
    private static BitmapImage? DecodeThumbnail(byte[] jpeg)
    {
        if (jpeg.Length == 0)
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            using (var stream = new MemoryStream(jpeg))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void UpdateEmptyHint() =>
        EmptyRosterTextBlock.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnWindowClosed(object sender, EventArgs e)
    {
        foreach (var poller in _pollers.Values)
        {
            poller.Dispose();
        }
        _pollers.Clear();
    }
}
