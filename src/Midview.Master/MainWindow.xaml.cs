using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Midview.Protocol;

namespace Midview.Master;

// The classroom console: one live thumbnail per machine in the teacher's
// classroom list; clicking one selects it, and "Ver a tela" or "Controlar"
// opens its screen. Testing and saving machines, one-off connections and
// site-blocking commands live in AddMachineWindow.
public partial class MainWindow : Window
{
    private readonly X509Certificate2 _masterCertificate = MasterIdentity.LoadOrCreate(MasterIdentity.DefaultCertificatePath);
    private readonly ObservableCollection<MachineTile> _tiles = new();
    private readonly Dictionary<MachineTile, ThumbnailPoller> _pollers = new();

    // At most one conversation per machine: asking again brings the open
    // one to the front instead of popping a second window up on the student.
    private readonly Dictionary<string, ChatWindow> _chatWindows = new(StringComparer.OrdinalIgnoreCase);

    // The same for every other window: one per action and machines (OpenOnce).
    private readonly OpenOnce<Window> _openWindows = new();

    // The teacher's power timetable; the pollers read it on their own
    // threads to hand it to each machine's Agent.
    private PowerSchedule? _powerSchedule;
    private AppBlockPolicy? _appBlockPolicy;

    private MachineTile? _selectedTile;

    // The machines whose screen is locked, each kept so by its own
    // connection; how each one last answered, for the status line.
    private readonly Dictionary<MachineTile, ScreenLockKeeper> _screenLocks = new();
    private readonly Dictionary<MachineTile, (bool Locked, string Detail)> _screenLockStatus = new();

    // Machines locked at least once since the teacher locked them: one that
    // isn't locked anymore got out (signed out, restarted, switched user).
    private readonly HashSet<MachineTile> _wereLocked = new();

    public MainWindow()
    {
        InitializeComponent();
        // Wide enough for the toolbar on one row, but never past a smaller screen.
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        MachinesItemsControl.ItemsSource = _tiles;
        _powerSchedule = LoadPowerSchedule();
        _appBlockPolicy = LoadAppBlockPolicy();

        foreach (var machine in LoadRoster())
        {
            InsertTile(_tiles.Count, machine);
        }
        UpdateEmptyHint();
    }

    // A click, left or right, only selects the machine. Watching its screen
    // or controlling it -- which locks the student's keyboard and mouse -- is
    // chosen on purpose with the toolbar's buttons or the tile's menu.
    private void OnMachineClicked(object sender, RoutedEventArgs e) => SelectTileOf(sender);

    private void OnMachineRightClicked(object sender, System.Windows.Input.MouseButtonEventArgs e) => SelectTileOf(sender);

    private void SelectTileOf(object sender)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            Select(tile);
        }
    }

    private void Select(MachineTile? tile)
    {
        if (_selectedTile is not null)
        {
            _selectedTile.IsSelected = false;
        }
        _selectedTile = tile;
        if (tile is not null)
        {
            tile.IsSelected = true;
        }
        ViewButton.IsEnabled = tile is not null;
        ControlButton.IsEnabled = tile is not null;
        StatusTextBlock.Text = tile is null ? string.Empty : $"Selecionada: {tile.Name}";
    }

    private void OnViewSelectedClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedTile is not null)
        {
            OpenScreen(_selectedTile, control: false);
        }
    }

    private void OnControlSelectedClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedTile is not null)
        {
            OpenScreen(_selectedTile, control: true);
        }
    }

    private void OnViewMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenScreen(tile, control: false);
        }
    }

    private void OnControlMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenScreen(tile, control: true);
        }
    }

    // One window per machine: asking again brings it back, and a click while
    // it is still connecting makes no second connection.
    private async void OpenScreen(MachineTile tile, bool control)
    {
        var machine = tile.Machine;
        var key = OpenOnce<Window>.Key("tela", new[] { machine.Name });
        if (_openWindows.TryGet(key, out var open))
        {
            BringBack(open);
            return;
        }
        if (!_openWindows.TryStartOpening(key))
        {
            return;
        }

        StatusTextBlock.Text = $"Conectando a {machine.Name}...";
        try
        {
            var client = await Task.Run(() => ConnectionClient.Connect(
                machine.HostAddress, ConnectionClient.SessionPort, machine.SessionThumbprint, _masterCertificate));

            StatusTextBlock.Text = string.Empty;
            var window = new ViewControlWindow(client, machine.Name, control);
            _openWindows.Add(key, window);
            window.Closed += (_, _) => _openWindows.Remove(key, window);
            window.Show();
        }
        catch (Exception ex)
        {
            _openWindows.StopOpening(key);
            StatusTextBlock.Text = $"Não foi possível conectar a {machine.Name}: {FailureText.Describe(ex)}";
        }
    }

    // The window for this action and these machines: the one already open,
    // brought back, or a new one from create.
    private void ShowOnce(string action, IEnumerable<KnownMachine> targets, Func<Window> create)
    {
        var key = OpenOnce<Window>.Key(action, targets.Select(machine => machine.Name));
        if (_openWindows.TryGet(key, out var open))
        {
            BringBack(open);
            return;
        }

        var window = create();
        _openWindows.Add(key, window);
        window.Closed += (_, _) => _openWindows.Remove(key, window);
        window.Show();
    }

    private static void BringBack(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
    }

    // One button for both: it locks the class, or, while any screen is
    // locked, gives them all back.
    private void OnScreenLockClassClicked(object sender, RoutedEventArgs e)
    {
        if (_screenLocks.Count > 0)
        {
            UnlockAll();
        }
        else
        {
            LockClass();
        }
    }

    private void LockClass()
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para bloquear.";
            return;
        }
        foreach (var tile in _tiles)
        {
            LockScreen(tile);
        }
        ShowScreenLockStatus();
    }

    private void UnlockAll()
    {
        foreach (var tile in _screenLocks.Keys.ToList())
        {
            UnlockScreen(tile);
        }
        StatusTextBlock.Text = "Telas desbloqueadas.";
    }

    private void UpdateScreenLockButton()
    {
        var (label, badge, toolTip) = ScreenLockText.ClassButton(anyLocked: _screenLocks.Count > 0);
        ScreenLockButton.Content = label;
        ScreenLockButton.Badge = badge;
        ScreenLockButton.ToolTip = toolTip;
    }

    private void OnLockMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            LockScreen(tile);
            ShowScreenLockStatus();
        }
    }

    private void OnUnlockMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            UnlockScreen(tile);
            StatusTextBlock.Text = $"Tela de {tile.Name} desbloqueada.";
        }
    }

    private void LockScreen(MachineTile tile)
    {
        if (_screenLocks.ContainsKey(tile))
        {
            return;
        }
        var keeper = new ScreenLockKeeper(tile.Machine, _masterCertificate);
        keeper.StatusChanged += (locked, detail) => Dispatcher.InvokeAsync(() =>
        {
            if (_screenLocks.TryGetValue(tile, out var current) && current == keeper)
            {
                var wasLocked = _wereLocked.Contains(tile);
                if (locked)
                {
                    _wereLocked.Add(tile);
                }
                _screenLockStatus[tile] = (locked, detail);
                var (text, alert) = ScreenLockText.Badge(locked, wasLocked, detail);
                tile.ShowScreenLock(text, alert);
                ShowScreenLockStatus();
            }
        });
        _screenLocks[tile] = keeper;
        tile.ShowScreenLock(ScreenLockText.Locking, alert: false);
        keeper.Start();
        UpdateScreenLockButton();
    }

    private void UnlockScreen(MachineTile tile)
    {
        if (_screenLocks.Remove(tile, out var keeper))
        {
            keeper.Dispose();
        }
        _screenLockStatus.Remove(tile);
        _wereLocked.Remove(tile);
        tile.ShowScreenLock(string.Empty, alert: false);
        UpdateScreenLockButton();
    }

    // "Telas bloqueadas: 12 de 14. Saíram do bloqueio: Aluno-03 (Sem
    // usuário logado). Não bloqueou: Aluno-07 (Offline)."
    private void ShowScreenLockStatus()
    {
        if (_screenLocks.Count == 0)
        {
            return;
        }
        var locked = _screenLockStatus.Values.Count(status => status.Locked);
        var notLocked = _screenLockStatus.Where(pair => !pair.Value.Locked).ToList();
        string Names(IEnumerable<KeyValuePair<MachineTile, (bool Locked, string Detail)>> machines) =>
            string.Join(", ", machines.Select(pair => $"{pair.Key.Name} ({pair.Value.Detail})"));
        var gotOut = notLocked.Where(pair => _wereLocked.Contains(pair.Key)).ToList();
        var failed = notLocked.Where(pair => !_wereLocked.Contains(pair.Key)).ToList();
        var waiting = _screenLocks.Count - _screenLockStatus.Count;
        StatusTextBlock.Text = $"Telas bloqueadas: {locked} de {_screenLocks.Count}."
            + (waiting > 0 ? $" Aguardando {waiting}." : string.Empty)
            + (gotOut.Count > 0 ? $" Saíram do bloqueio: {Names(gotOut)}." : string.Empty)
            + (failed.Count > 0 ? $" Não bloqueou: {Names(failed)}." : string.Empty);
    }

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
        ShowOnce("arquivo", targets, () => new SendFileWindow(targets, _masterCertificate) { Owner = this });

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
        ShowOnce("recolher", targets, () => new CollectFilesWindow(targets, _masterCertificate) { Owner = this });

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
        ShowOnce("fechar", targets, () => new CloseAppsWindow(targets, _masterCertificate) { Owner = this });

    private void OnBlockAppsClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para bloquear programas.";
            return;
        }
        var targets = _tiles.Select(tile => tile.Machine).ToList();
        ShowOnce("programas", targets, () => new AppBlockWindow(
            targets, _masterCertificate,
            policy =>
            {
                Volatile.Write(ref _appBlockPolicy, policy);
                SaveAppBlockPolicy(policy);
            })
        {
            Owner = this,
        });
    }

    private void OnSiteBlockClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para bloquear sites.";
            return;
        }
        var targets = _tiles.Select(tile => tile.Machine).ToList();
        ShowOnce("sites", targets, () => new SiteBlockWindow(targets, _masterCertificate) { Owner = this });
    }

    private void OnSiteBlockMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            ShowOnce("sites", new[] { tile.Machine }, () => new SiteBlockWindow(new[] { tile.Machine }, _masterCertificate) { Owner = this });
        }
    }

    private void OnBlockAppsMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            ShowOnce("programas", new[] { tile.Machine }, () => new AppBlockWindow(new[] { tile.Machine }, _masterCertificate) { Owner = this });
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
            StatusTextBlock.Text = $"O bloqueio de programas não pôde ser lido: {ErrorText.Describe(ex)}";
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
            StatusTextBlock.Text = $"O bloqueio foi enviado, mas não pôde ser guardado neste PC: {ErrorText.Describe(ex)}";
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

    private void OnRestartClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para reiniciar.";
            return;
        }
        OpenPowerWindow(_tiles.Select(tile => tile.Machine).ToList(), PowerAction.Restart);
    }

    private void OnRestartMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenPowerWindow(new[] { tile.Machine }, PowerAction.Restart);
        }
    }

    // "Desligar" and "Reiniciar" each have their own, set to their action.
    private void OpenPowerWindow(IReadOnlyList<KnownMachine> targets, PowerAction action = PowerAction.Shutdown) =>
        ShowOnce(action == PowerAction.Restart ? "reiniciar" : "desligar", targets,
            () => new PowerWindow(targets, _masterCertificate, action) { Owner = this });

    // For the whole room, whichever machines are in it.
    private void OnScheduleClicked(object sender, RoutedEventArgs e) =>
        ShowOnce("agendar", Array.Empty<KnownMachine>(), () => new ScheduleWindow(
            () => _tiles.Select(tile => tile.Machine).ToList(), Volatile.Read(ref _powerSchedule), _masterCertificate,
            schedule => Volatile.Write(ref _powerSchedule, schedule))
        {
            Owner = this,
        });

    // One click, no window: turning machines on can't hurt (one already on
    // ignores the request), so it goes out at once.
    private void OnWakeClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para ligar.";
            return;
        }
        Wake(_tiles.Select(tile => tile.Machine).ToList());
    }

    private void OnWakeMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            Wake(new[] { tile.Machine });
        }
    }

    // Over the network (Wake-on-LAN), to every machine whose network card
    // the Master has learned (RememberMacAddress); the status bar says who.
    private async void Wake(IReadOnlyList<KnownMachine> machines)
    {
        var known = machines
            .Select(machine => (machine.Name, MacAddress: WakeOnLan.TryParseMacAddress(machine.MacAddress, out var mac) ? mac : null))
            .ToList();
        var toWake = known.Where(machine => machine.MacAddress is not null).ToList();
        var unknown = known.Where(machine => machine.MacAddress is null).Select(machine => machine.Name).ToList();

        StatusTextBlock.Text = "Enviando o pedido para ligar...";
        var (sent, lastError) = toWake.Count == 0
            ? (0, null)
            : await Task.Run(() => WakeOnLanSender.Send(
                toWake.Select(machine => machine.MacAddress!).ToList(),
                WakeOnLanSender.BroadcastTargets(WakeOnLanSender.LocalNetworks())));
        StatusTextBlock.Text = WakeText.Summary(toWake.Select(machine => machine.Name).ToList(), unknown, sent > 0, lastError);
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
            StatusTextBlock.Text = $"A placa de rede de {tile.Name} não pôde ser gravada na lista da sala: {ErrorText.Describe(ex)}";
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
        ShowOnce("mensagem", targets, () => new SendMessageWindow(targets, _masterCertificate) { Owner = this });

    private void OnEditMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenAddMachineWindow(tile.Machine);
        }
    }

    private void OpenAddMachineWindow(KnownMachine? existing) =>
        ShowOnce("avancado", existing is null ? Array.Empty<KnownMachine>() : new[] { existing },
            () => new AddMachineWindow(_masterCertificate, SaveMachine, existing) { Owner = this });

    private MachinesWindow? _machinesWindow;

    private void OnMachinesClicked(object sender, RoutedEventArgs e) => OpenMachinesWindow(search: false);

    private void OnSearchNetworkClicked(object sender, RoutedEventArgs e) => OpenMachinesWindow(search: true);

    // One at a time: asking again brings it to the front.
    private async void OpenMachinesWindow(bool search)
    {
        var window = _machinesWindow;
        if (window is null)
        {
            window = new MachinesWindow(
                _masterCertificate, () => _tiles.Select(tile => tile.Machine).ToList(), AddPairedMachine, RefreshAddresses,
                () => OpenAddMachineWindow(existing: null)) { Owner = this };
            window.Closed += (_, _) => _machinesWindow = null;
            _machinesWindow = window;
            window.Show();
        }
        else
        {
            window.Activate();
        }

        if (search)
        {
            await window.SearchAsync();
        }
    }

    private void OnRemoveMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTile tile)
        {
            return;
        }

        // Gone already if a save from an AddMachineWindow replaced it.
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
            StatusTextBlock.Text = $"Removida da grade, mas a lista não pôde ser gravada em disco: {ErrorText.Describe(ex)}";
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

    // Called by MachinesWindow when a pairing is confirmed (see Roster.Place).
    // The grid changes first; a failure to write the list then propagates
    // for MachinesWindow to report.
    private void AddPairedMachine(KnownMachine machine)
    {
        var (index, replaces, placed) = Roster.Place(_tiles.Select(tile => tile.Machine).ToList(), machine);
        if (replaces)
        {
            RemoveTileAt(index);
        }
        InsertTile(index, placed);
        UpdateEmptyHint();
        SaveRoster();
    }

    // Called by MachinesWindow after a search: machines found at another
    // address (see Roster.NewAddresses). Returns how many moved.
    private int RefreshAddresses(IReadOnlyList<FoundMachine> found)
    {
        var changes = Roster.NewAddresses(_tiles.Select(tile => tile.Machine).ToList(), found);
        foreach (var (index, updated) in changes)
        {
            RemoveTileAt(index);
            InsertTile(index, updated);
        }
        if (changes.Count > 0)
        {
            SaveRoster();
        }
        return changes.Count;
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
            StatusTextBlock.Text = $"A lista da sala não pôde ser lida: {ErrorText.Describe(ex)}";
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
            StatusTextBlock.Text = $"O agendamento de ligar e desligar não pôde ser lido: {ErrorText.Describe(ex)}";
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
        if (tile == _selectedTile)
        {
            Select(null);
        }
        UnlockScreen(tile);
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
        EmptyRosterPanel.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnWindowClosed(object sender, EventArgs e)
    {
        // Closing the teacher's program unlocks every screen it locked.
        foreach (var keeper in _screenLocks.Values)
        {
            keeper.Dispose();
        }
        _screenLocks.Clear();

        foreach (var poller in _pollers.Values)
        {
            poller.Dispose();
        }
        _pollers.Clear();
    }
}
