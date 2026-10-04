using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Midview.Protocol;

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

    // At most one conversation per machine: asking again brings the open
    // one to the front instead of popping a second window up on the student.
    private readonly Dictionary<string, ChatWindow> _chatWindows = new(StringComparer.OrdinalIgnoreCase);

    // The teacher's power timetable; the pollers read it on their own
    // threads to hand it to each machine's Agent.
    private PowerSchedule? _powerSchedule;
    private AppBlockPolicy? _appBlockPolicy;

    public MainWindow()
    {
        InitializeComponent();
        MachinesItemsControl.ItemsSource = _tiles;
        _powerSchedule = LoadPowerSchedule();
        _appBlockPolicy = LoadAppBlockPolicy();

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

    private void OnFileClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para receber o arquivo.";
            return;
        }
        OpenSendFileWindow(_tiles.Select(tile => tile.Machine).ToList());
    }

    private void OnFileMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenSendFileWindow(new[] { tile.Machine });
        }
    }

    private void OpenSendFileWindow(IReadOnlyList<KnownMachine> targets) =>
        new SendFileWindow(targets, _masterCertificate) { Owner = this }.Show();

    private void OnCollectClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para recolher arquivos.";
            return;
        }
        OpenCollectFilesWindow(_tiles.Select(tile => tile.Machine).ToList());
    }

    private void OnCollectMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenCollectFilesWindow(new[] { tile.Machine });
        }
    }

    private void OpenCollectFilesWindow(IReadOnlyList<KnownMachine> targets) =>
        new CollectFilesWindow(targets, _masterCertificate) { Owner = this }.Show();

    private void OnCloseAppsClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para fechar programas.";
            return;
        }
        OpenCloseAppsWindow(_tiles.Select(tile => tile.Machine).ToList());
    }

    private void OnCloseAppsMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenCloseAppsWindow(new[] { tile.Machine });
        }
    }

    private void OpenCloseAppsWindow(IReadOnlyList<KnownMachine> targets) =>
        new CloseAppsWindow(targets, _masterCertificate) { Owner = this }.Show();

    private void OnBlockAppsClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para bloquear programas.";
            return;
        }
        new AppBlockWindow(
            _tiles.Select(tile => tile.Machine).ToList(), _masterCertificate,
            policy =>
            {
                Volatile.Write(ref _appBlockPolicy, policy);
                SaveAppBlockPolicy(policy);
            })
        {
            Owner = this,
        }.Show();
    }

    private void OnBlockAppsMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            new AppBlockWindow(new[] { tile.Machine }, _masterCertificate) { Owner = this }.Show();
        }
    }

    private AppBlockPolicy? LoadAppBlockPolicy()
    {
        try
        {
            return AppBlockFile.Load(AppBlockFile.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = $"O bloqueio de programas não pôde ser lido: {ex.Message}";
            return null;
        }
    }

    private void SaveAppBlockPolicy(AppBlockPolicy policy)
    {
        try
        {
            AppBlockFile.Save(AppBlockFile.DefaultPath, policy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = $"O bloqueio foi enviado, mas não pôde ser guardado neste PC: {ex.Message}";
        }
    }

    private void OnPowerClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para desligar.";
            return;
        }
        OpenPowerWindow(_tiles.Select(tile => tile.Machine).ToList());
    }

    private void OnPowerMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenPowerWindow(new[] { tile.Machine });
        }
    }

    private void OpenPowerWindow(IReadOnlyList<KnownMachine> targets) =>
        new PowerWindow(targets, _masterCertificate) { Owner = this }.Show();

    private void OnScheduleClicked(object sender, RoutedEventArgs e) =>
        new ScheduleWindow(
            () => _tiles.Select(tile => tile.Machine).ToList(), Volatile.Read(ref _powerSchedule), _masterCertificate,
            schedule => Volatile.Write(ref _powerSchedule, schedule))
        {
            Owner = this,
        }.Show();

    private void OnWakeClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para ligar.";
            return;
        }
        new WakeWindow(() => _tiles.Select(tile => tile.Machine).ToList(), $"Na turma toda ({_tiles.Count} máquinas)")
        {
            Owner = this,
        }.Show();
    }

    private void OnWakeMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            new WakeWindow(() => new[] { tile.Machine }, $"Em: {tile.Name}") { Owner = this }.Show();
        }
    }

    // From a tile's poller, once per Master start. Only written to disk when
    // it's new or changed (a replaced network card).
    private void RememberMacAddress(MachineTile tile, string macAddress)
    {
        if (!_tiles.Contains(tile) || tile.Machine.MacAddress == macAddress)
        {
            return;
        }

        tile.RememberMacAddress(macAddress);
        try
        {
            SaveRoster();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = $"A placa de rede de {tile.Name} não pôde ser gravada na lista da sala: {ex.Message}";
        }
    }

    private void OnChatMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTile tile)
        {
            return;
        }

        if (_chatWindows.TryGetValue(tile.Machine.Name, out var open))
        {
            open.Activate();
            return;
        }

        var chat = new ChatWindow(tile.Machine, _masterCertificate) { Owner = this };
        chat.Closed += (_, _) => _chatWindows.Remove(chat.Machine.Name);
        _chatWindows.Add(tile.Machine.Name, chat);
        chat.Show();
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

    private void OnLabInstallClicked(object sender, RoutedEventArgs e) =>
        new LabInstallWindow(_masterCertificate, ImportMachines, () => _tiles.Select(tile => tile.Machine).ToList()) { Owner = this }.Show();

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
            if (machine.IsSameEntryAs(original) || string.Equals(machine.Name, updated.Name, StringComparison.OrdinalIgnoreCase))
            {
                // The window doesn't show the network card; saving from it
                // must not forget the one learned.
                updated = updated.KeepingMacAddressOf(machine);
                RemoveTileAt(i);
                position = i;
            }
        }

        InsertTile(position, updated);
        UpdateEmptyHint();
        SaveRoster();
    }

    // Called by LabInstallWindow. As in SaveMachine, the same name means the
    // same machine: it's replaced in place, and new ones go at the end. The
    // file is written once for the whole import; a failure to write it
    // propagates for LabInstallWindow to report.
    private (int Added, int Updated, int Unchanged) ImportMachines(IReadOnlyList<KnownMachine> machines)
    {
        int added = 0, updated = 0, unchanged = 0;
        foreach (var imported in machines)
        {
            var index = _tiles.ToList().FindIndex(tile =>
                string.Equals(tile.Machine.Name, imported.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                InsertTile(_tiles.Count, imported);
                added++;
                continue;
            }

            // A machine file from an older installer has no network card.
            var machine = imported.KeepingMacAddressOf(_tiles[index].Machine);
            if (_tiles[index].Machine == machine)
            {
                unchanged++;
            }
            else
            {
                RemoveTileAt(index);
                InsertTile(index, machine);
                updated++;
            }
        }

        UpdateEmptyHint();
        SaveRoster();
        return (added, updated, unchanged);
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

    private PowerSchedule? LoadPowerSchedule()
    {
        try
        {
            return PowerScheduleFile.Load(PowerScheduleFile.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = $"O agendamento de ligar e desligar não pôde ser lido: {ex.Message}";
            return null;
        }
    }

    private void SaveRoster() =>
        KnownMachinesFile.Save(KnownMachinesFile.DefaultPath, _tiles.Select(tile => tile.Machine));

    private void InsertTile(int index, KnownMachine machine)
    {
        var tile = new MachineTile(machine);
        _tiles.Insert(index, tile);

        var poller = new ThumbnailPoller(
            machine, _masterCertificate,
            powerSchedule: () => Volatile.Read(ref _powerSchedule),
            appBlockPolicy: () => Volatile.Read(ref _appBlockPolicy));
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
        poller.MacAddressLearned += macAddress => Dispatcher.InvokeAsync(() => RememberMacAddress(tile, macAddress));
        poller.AppBlockObserved += status => Dispatcher.InvokeAsync(() =>
            tile.BlockBadge = AppBlockText.Badge(status, classBlockExpected: Volatile.Read(ref _appBlockPolicy) is { IsActive: true }));
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
