using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Midview.Protocol;

namespace Midview.Master;

// The classroom console: one live thumbnail per machine in the teacher's
// classroom list; clicking one selects it, and "Ver a tela" or "Controlar"
// opens its screen. Testing and saving machines, one-off connections and
// site-blocking commands live in AddMachineWindow. Several machines can be
// selected (Ctrl/Shift+click, or a rectangle dragged over the grid's empty
// space), and a tile's menu then acts on all of them.
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

    // The grid's selection: a plain click picks one, Ctrl/Shift add more, and a
    // rectangle dragged over the grid's empty space picks every tile it
    // touches. "Ver a tela" and "Controlar" need exactly one; a tile's menu
    // acts on all of them (TargetsOf).
    private readonly GridSelection<MachineTile> _selection;

    // A press on the grid's empty space that may become a selection rectangle:
    // where it started (on GridSurface, so it scrolls with the tiles), whether
    // Ctrl was held (the rectangle then adds to the selection), and whether the
    // mouse has moved far enough yet to be a drag rather than a click.
    private Point? _bandStart;
    private bool _bandAdds;
    private bool _bandDragging;

    // The tile menu's items shown only with several machines selected, and
    // those for one machine only (MainWindow.xaml).
    private const string SeveralOnlyTag = "varias";
    private const string OneOnlyTag = "uma";
    // The menu's one item that locks or gives back the screen (OnMachineMenuOpening words it).
    private const string ScreenLockTag = "bloqueio";

    // The machines whose screen is locked, each kept so by its own
    // connection; how each one last answered, for the status line.
    private readonly Dictionary<MachineTile, ScreenLockKeeper> _screenLocks = new();
    private readonly Dictionary<MachineTile, (bool Locked, string Detail)> _screenLockStatus = new();

    // Machines locked at least once since the teacher locked them: one that
    // isn't locked anymore got out (signed out, restarted, switched user).
    private readonly HashSet<MachineTile> _wereLocked = new();

    // "Só online": the grid shows only the machines that answer (GridFilter).
    // _shownTiles is the grid's own list of _tiles (its default view),
    // filtered and kept up to date as machines answer or stop answering;
    // selecting only ever reaches the tiles it shows.
    private bool _onlineOnly;
    private readonly ListCollectionView _shownTiles;

    public MainWindow()
    {
        InitializeComponent();
        // Wide enough for the toolbar on one row, but never past a smaller screen.
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        MachinesItemsControl.ItemsSource = _tiles;
        _shownTiles = (ListCollectionView)CollectionViewSource.GetDefaultView(_tiles);
        _shownTiles.Filter = item => GridFilter.Shows((MachineTile)item, _onlineOnly);
        _shownTiles.IsLiveFiltering = true;
        _shownTiles.LiveFilteringProperties.Add(nameof(MachineTile.Presence));
        ((INotifyCollectionChanged)_shownTiles).CollectionChanged += OnShownTilesChanged;
        _selection = new GridSelection<MachineTile>(new ShownTiles(_shownTiles));
        _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var gridView = GridViewFile.Load(GridViewFile.DefaultPath);
        LoadTileLayout(gridView);
        _onlineOnly = gridView.OnlineOnly;
        OnlineOnlyButton.IsChecked = _onlineOnly;
        _powerSchedule = LoadPowerSchedule();
        _appBlockPolicy = LoadAppBlockPolicy();
        _classSync = StartClassFolder();
        StartClassHome();

        foreach (var machine in LoadRoster())
        {
            InsertTile(_tiles.Count, machine);
        }
        UpdateEmptyHint();
        ShowClassLesson();
    }

    // A click, left or right, only selects the machine. Watching its screen
    // or controlling it -- which locks the student's keyboard and mouse -- is
    // chosen on purpose with the toolbar's buttons or the tile's menu.
    private void OnMachineClicked(object sender, RoutedEventArgs e) => SelectTileOf(sender);

    // The right button keeps a selection of several that the tile is part of,
    // for its menu to act on all of them; otherwise it selects just this tile.
    private void OnMachineRightClicked(object sender, MouseButtonEventArgs e) => RightClickTileOf(sender);

    private void SelectTileOf(object sender)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTile tile)
        {
            return;
        }
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            _selection.ShiftClick(tile);
        }
        else if (modifiers.HasFlag(ModifierKeys.Control))
        {
            _selection.CtrlClick(tile);
        }
        else
        {
            _selection.Click(tile);
        }
        RefreshSelection();
    }

    private void RightClickTileOf(object sender)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            _selection.RightClick(tile);
            RefreshSelection();
        }
    }

    // Paints the tiles, enables the buttons that need one machine, and shows
    // what is selected -- after any change to _selection.
    private void RefreshSelection()
    {
        var selected = _selection.Selected;
        var marked = new HashSet<MachineTile>(selected);
        foreach (var tile in _tiles)
        {
            tile.IsSelected = marked.Contains(tile);
        }

        ViewButton.IsEnabled = selected.Count == 1;
        ControlButton.IsEnabled = selected.Count == 1;
        StatusTextBlock.Text = selected.Count switch
        {
            0 => string.Empty,
            1 => $"Selecionada: {selected[0].Name}",
            _ => $"Selecionadas: {selected.Count}",
        };
    }

    // A click on empty space or Esc. Nothing selected, nothing to change: the
    // status bar may still be saying how the last action went.
    private void ClearSelection()
    {
        if (_selection.Selected.Count == 0)
        {
            return;
        }
        _selection.Clear();
        RefreshSelection();
    }

    // Esc lets go of the selection, and of a rectangle being dragged.
    // Ctrl +, - and 0 resize the tiles.
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && TileWidthForKey(e.Key) is { } width)
        {
            ResizeTiles(width);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape)
        {
            return;
        }
        GridSurface.ReleaseMouseCapture();
        EndBand();
        // The tile last clicked keeps keyboard focus, and with it a focus
        // border: off it, so nothing on the grid looks selected after Esc.
        if (GridSurface.IsKeyboardFocusWithin)
        {
            MachinesScrollViewer.Focus();
        }
        ClearSelection();
        e.Handled = true;
    }

    private void OnOnlineOnlyClicked(object sender, RoutedEventArgs e) => ShowOnlineOnly(OnlineOnlyButton.IsChecked == true);

    // The button under "Nenhuma máquina online agora".
    private void OnShowAllMachinesClicked(object sender, RoutedEventArgs e) => ShowOnlineOnly(false);

    // The teacher turned "Só online" on or off: shown now, and the next time
    // Midview opens.
    private void ShowOnlineOnly(bool onlineOnly)
    {
        OnlineOnlyButton.IsChecked = onlineOnly;
        if (onlineOnly == _onlineOnly)
        {
            return;
        }
        _onlineOnly = onlineOnly;
        // The view's own Refresh: MachinesItemsControl.Items.Refresh() left
        // the hidden tiles in when tried.
        _shownTiles.Refresh();
        UpdateEmptyHint();
        SaveGridView();
    }

    // A tile the grid stopped showing ("Só online") leaves the selection, so
    // "Ver a tela", "Controlar" and a tile's menu never act on a hidden one.
    // Fewer or more shown, "Visualizar todos" fits them again.
    private void OnShownTilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _selection.Prune();
        if (!_selection.Selected.SequenceEqual(_tiles.Where(tile => tile.IsSelected)))
        {
            RefreshSelection();
        }
        FitSoon();
    }

    // The grid's filtered list, as GridSelection reads it: live, in grid order.
    private sealed class ShownTiles : IReadOnlyList<MachineTile>
    {
        private readonly ListCollectionView _view;

        public ShownTiles(ListCollectionView view) => _view = view;

        public int Count => _view.Count;

        public MachineTile this[int index] => (MachineTile)_view.GetItemAt(index);

        public IEnumerator<MachineTile> GetEnumerator() => _view.Cast<MachineTile>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // A press on the grid's empty space -- a tile, being a button, takes its
    // own presses, and the scroll bar is outside GridSurface. Moved far enough
    // it becomes a selection rectangle (UpdateBand); released before that it
    // is a click (OnGridSurfaceMouseUp).
    private void OnGridSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        _bandStart = e.GetPosition(GridSurface);
        _bandAdds = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _bandDragging = false;
        _selection.BeginBand();
        // Off any tile clicked before, which would keep its focus border
        // even once the rectangle leaves it out.
        MachinesScrollViewer.Focus();
        GridSurface.CaptureMouse();
        e.Handled = true;
    }

    private void OnGridSurfaceMouseMove(object sender, MouseEventArgs e)
    {
        if (_bandStart is { } start && GridSurface.IsMouseCaptured)
        {
            UpdateBand(start, e.GetPosition(GridSurface));
        }
    }

    // A plain click on empty space clears the selection. With Ctrl held it
    // leaves it, as a Ctrl-drag that touched no tile would.
    private void OnGridSurfaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_bandStart is null)
        {
            return;
        }
        var click = !_bandDragging;
        var adds = _bandAdds;
        GridSurface.ReleaseMouseCapture();
        EndBand();
        if (click && !adds)
        {
            ClearSelection();
        }
        e.Handled = true;
    }

    // Released, or taken away (another window came up): the rectangle goes,
    // and the selection stays as the rectangle last left it. LostMouseCapture
    // bubbles up from the tile buttons, which capture the mouse while pressed:
    // only the surface's own capture ending ends the rectangle.
    private void OnGridSurfaceLostCapture(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, GridSurface))
        {
            EndBand();
            EndDrag();
        }
    }

    // The wheel can scroll the grid during a drag: the rectangle's far corner
    // stays under the mouse, over the tiles that moved there.
    private void OnMachinesScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (_bandStart is { } start && GridSurface.IsMouseCaptured)
        {
            UpdateBand(start, Mouse.GetPosition(GridSurface));
        }
    }

    private void UpdateBand(Point start, Point current)
    {
        if (!_bandDragging)
        {
            if (!RubberBand.IsDrag(start, current))
            {
                return;
            }
            _bandDragging = true;
            SelectionBand.Visibility = Visibility.Visible;
        }

        var band = RubberBand.Between(start, current);
        Canvas.SetLeft(SelectionBand, band.X);
        Canvas.SetTop(SelectionBand, band.Y);
        SelectionBand.Width = band.Width;
        SelectionBand.Height = band.Height;

        // Only a change repaints and rewrites the status bar: the mouse moves
        // far more often than the rectangle reaches another tile.
        var before = _selection.Selected;
        _selection.Band(RubberBand.Touched(band, TileBounds()), _bandAdds);
        if (!before.SequenceEqual(_selection.Selected))
        {
            RefreshSelection();
        }
    }

    private void EndBand()
    {
        _bandStart = null;
        _bandDragging = false;
        SelectionBand.Visibility = Visibility.Collapsed;
    }

    // Each tile's card (the button, without its margin) on GridSurface, in
    // grid order, the same surface the rectangle is measured on.
    private IEnumerable<(MachineTile Item, Rect Bounds)> TileBounds()
    {
        foreach (var tile in _tiles)
        {
            if (MachinesItemsControl.ItemContainerGenerator.ContainerFromItem(tile) is DependencyObject container
                && VisualTreeHelper.GetChildrenCount(container) > 0
                && VisualTreeHelper.GetChild(container, 0) is FrameworkElement card
                && card.IsVisible)
            {
                yield return (tile, card.TransformToAncestor(GridSurface).TransformBounds(new Rect(card.RenderSize)));
            }
        }
    }

    // What a tile's menu acts on: every selected tile, in grid order, when
    // this one is part of a selection of several; otherwise just this one.
    private IReadOnlyList<MachineTile> TargetsOf(MachineTile tile) => _selection.TargetsOf(tile);

    private IReadOnlyList<KnownMachine> MachinesOf(MachineTile tile) =>
        TargetsOf(tile).Select(target => target.Machine).ToList();

    // Also reached from the keyboard (the menu key), with no right-click
    // before it to settle the selection.
    private void OnMachineMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (element.DataContext is not MachineTile tile || element.ContextMenu is not { } menu)
        {
            return;
        }
        RightClickTileOf(sender);
        var targets = TargetsOf(tile);
        ShowMenuFor(menu, targets.Count);
        foreach (var item in menu.Items.OfType<MenuItem>().Where(item => Equals(item.Tag, ScreenLockTag)))
        {
            item.Header = ScreenLockText.MachineMenuItem(targets.Any(_screenLocks.ContainsKey));
        }
    }

    // With several machines the menu first says how many, and leaves out what
    // only makes sense for one (watching, controlling, chatting, uninstalling,
    // editing, removing); with one it is the menu it always was.
    private static void ShowMenuFor(ContextMenu menu, int machines)
    {
        var several = machines > 1;
        foreach (var item in menu.Items.OfType<Control>())
        {
            switch (item.Tag)
            {
                case SeveralOnlyTag:
                    item.Visibility = several ? Visibility.Visible : Visibility.Collapsed;
                    if (item is MenuItem header)
                    {
                        header.Header = $"{machines} máquinas selecionadas";
                    }
                    break;
                case OneOnlyTag:
                    item.Visibility = several ? Visibility.Collapsed : Visibility.Visible;
                    break;
            }
        }
    }

    private void OnViewSelectedClicked(object sender, RoutedEventArgs e)
    {
        if (_selection.Selected is [var tile])
        {
            OpenScreen(tile, control: false);
        }
    }

    private void OnControlSelectedClicked(object sender, RoutedEventArgs e)
    {
        if (_selection.Selected is [var tile])
        {
            OpenScreen(tile, control: true);
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
            var (client, endpoint) = await Task.Run(() => ConnectForScreen(machine, control));

            StatusTextBlock.Text = string.Empty;
            var window = new ViewControlWindow(client, machine.Name, control, endpoint, machine, _masterCertificate);
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

    // How long the helper gets to answer before control falls back to the
    // Session: a machine with no helper (or one its firewall hides) must not
    // make the teacher wait.
    private static readonly TimeSpan HelperConnectTimeout = TimeSpan.FromSeconds(2);

    // Watching always uses the Session. Controlling tries the helper first,
    // which can follow the screen onto the UAC prompt, and on any failure --
    // refused, timed out, a handshake that doesn't trust it -- connects the
    // Session as it always has, so the helper never stands between the
    // teacher and a working connection. Runs off the window's thread.
    private (ConnectionClient Client, ControlEndpoint Endpoint) ConnectForScreen(KnownMachine machine, bool control)
    {
        if (control && !string.IsNullOrEmpty(machine.AgentThumbprint))
        {
            try
            {
                var (_, helperPort, helperThumbprint) = ControlChoice.Choose(machine, helperReachable: true);
                var helper = ConnectionClient.Connect(
                    machine.HostAddress, helperPort, helperThumbprint, _masterCertificate, HelperConnectTimeout);
                return (helper, ControlEndpoint.Helper);
            }
            catch (Exception)
            {
                // Falls through to the Session.
            }
        }

        var (endpoint, port, thumbprint) = ControlChoice.Choose(machine, helperReachable: false);
        var session = ConnectionClient.Connect(machine.HostAddress, port, thumbprint, _masterCertificate);
        return (session, endpoint);
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
        OwnedWindow.ReturnToOwnerWhenClosed(window);
        window.Show();
    }

    // Targets are machines of the classroom list, each once, so as many of
    // them as tiles is all of them: the windows then say "turma toda"
    // (TargetsText) instead of naming them.
    private bool IsWholeClass(IReadOnlyList<KnownMachine> targets) => targets.Count == _tiles.Count;

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

    // One item for both, like the toolbar's button: while any of the machines
    // it acts on is locked it gives them back, otherwise it locks them.
    private void OnScreenLockMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTile tile)
        {
            return;
        }
        var targets = TargetsOf(tile);
        if (targets.Any(_screenLocks.ContainsKey))
        {
            foreach (var target in targets)
            {
                UnlockScreen(target);
            }
            StatusTextBlock.Text = targets.Count == 1
                ? $"Tela de {tile.Name} desbloqueada."
                : $"Telas de {targets.Count} máquinas desbloqueadas.";
        }
        else
        {
            foreach (var target in targets)
            {
                LockScreen(target);
            }
            ShowScreenLockStatus();
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
            OpenSendMessageWindow(MachinesOf(tile));
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
            OpenSendFileWindow(MachinesOf(tile));
        }
    }

    private void OpenSendFileWindow(IReadOnlyList<KnownMachine> targets) =>
        ShowOnce("arquivo", targets, () => new SendFileWindow(targets, IsWholeClass(targets), _masterCertificate) { Owner = this });

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
            OpenCollectFilesWindow(MachinesOf(tile));
        }
    }

    private void OpenCollectFilesWindow(IReadOnlyList<KnownMachine> targets) =>
        ShowOnce("recolher", targets, () => new CollectFilesWindow(targets, IsWholeClass(targets), _masterCertificate) { Owner = this });

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
            OpenCloseAppsWindow(MachinesOf(tile));
        }
    }

    private void OpenCloseAppsWindow(IReadOnlyList<KnownMachine> targets) =>
        ShowOnce("fechar", targets, () => new CloseAppsWindow(targets, IsWholeClass(targets), _masterCertificate) { Owner = this });

    private void OnBlockAppsClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para bloquear programas.";
            return;
        }
        OpenAppBlockWindow(_tiles.Select(tile => tile.Machine).ToList());
    }

    // The block is remembered and resent (onClassPolicyApplied) only for the
    // whole class, whether it came from the toolbar or from a tile's menu with
    // every machine selected: both are the same window (ShowOnce's key).
    private void OpenAppBlockWindow(IReadOnlyList<KnownMachine> targets)
    {
        var wholeClass = IsWholeClass(targets);
        ShowOnce("programas", targets, () => new AppBlockWindow(
            targets, wholeClass, _masterCertificate,
            wholeClass
                ? policy =>
                {
                    Volatile.Write(ref _appBlockPolicy, policy);
                    SaveAppBlockPolicy(policy);
                }
                : null)
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
        OpenSiteBlockWindow(_tiles.Select(tile => tile.Machine).ToList());
    }

    private void OnSiteBlockMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenSiteBlockWindow(MachinesOf(tile));
        }
    }

    private void OpenSiteBlockWindow(IReadOnlyList<KnownMachine> targets) =>
        ShowOnce("sites", targets, () => new SiteBlockWindow(targets, IsWholeClass(targets), _masterCertificate) { Owner = this });

    private void OnLabConfigClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para configurar.";
            return;
        }
        OpenLabConfigWindow(_tiles.Select(tile => tile.Machine).ToList());
    }

    private void OnLabConfigMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenLabConfigWindow(MachinesOf(tile));
        }
    }

    private void OpenLabConfigWindow(IReadOnlyList<KnownMachine> targets) =>
        ShowOnce("painel", targets, () => new LabConfigWindow(targets, IsWholeClass(targets), _masterCertificate) { Owner = this });

    // On the tiles' menu only, for the machines selected (drag over the grid
    // for the whole room): a toolbar button would wrap the toolbar onto a
    // second row on a 1366-pixel screen.
    private void OnInstallClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para instalar o programa.";
            return;
        }
        OpenInstallWindow(_tiles.Select(tile => tile.Machine).ToList());
    }

    private void OnInstallMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenInstallWindow(MachinesOf(tile));
        }
    }

    private void OpenInstallWindow(IReadOnlyList<KnownMachine> targets) =>
        ShowOnce("instalar", targets, () => new InstallWindow(targets, IsWholeClass(targets), _masterCertificate) { Owner = this });

    private void OnUninstallClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para desinstalar programas.";
            return;
        }
        var classTargets = _tiles.Select(t => t.Machine).ToList();
        var listFrom = classTargets[0];
        ShowOnce("desinstalar", new[] { listFrom },
            () => new UninstallWindow(listFrom, classTargets, _masterCertificate) { Owner = this });
    }

    private void OnUninstallMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            var classTargets = _tiles.Select(t => t.Machine).ToList();
            ShowOnce("desinstalar", new[] { tile.Machine },
                () => new UninstallWindow(tile.Machine, classTargets, _masterCertificate) { Owner = this });
        }
    }

    private void OnBlockAppsMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            OpenAppBlockWindow(MachinesOf(tile));
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
            OpenPowerWindow(MachinesOf(tile));
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
            OpenPowerWindow(MachinesOf(tile), PowerAction.Restart);
        }
    }

    // "Desligar" and "Reiniciar" each have their own, set to their action.
    private void OpenPowerWindow(IReadOnlyList<KnownMachine> targets, PowerAction action = PowerAction.Shutdown) =>
        ShowOnce(action == PowerAction.Restart ? "reiniciar" : "desligar", targets,
            () => new PowerWindow(targets, IsWholeClass(targets), _masterCertificate, action) { Owner = this });

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
            Wake(MachinesOf(tile));
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
        PublishRoster();
        try
        {
            SaveRoster();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = $"A placa de rede de {tile.Name} não pôde ser gravada na lista da sala: {ErrorText.Describe(ex)}";
        }
    }

    private void OnChatClassClicked(object sender, RoutedEventArgs e)
    {
        if (_tiles.Count == 0)
        {
            StatusTextBlock.Text = "Nenhuma máquina na lista da sala para conversar.";
            return;
        }
        // One class chat at a time, whoever is in the room list now: a second
        // one would pop a second chat up on every student's screen. Machines
        // added meanwhile join when it's closed and opened again.
        var targets = _tiles.Select(tile => tile.Machine).ToList();
        ShowOnce("bate-papo", Array.Empty<KnownMachine>(), () => new ClassChatWindow(targets, _masterCertificate) { Owner = this });
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
        OwnedWindow.ReturnToOwnerWhenClosed(chat);
        chat.Show();
    }

    private void OpenSendMessageWindow(IReadOnlyList<KnownMachine> targets) =>
        ShowOnce("mensagem", targets, () => new SendMessageWindow(targets, IsWholeClass(targets), _masterCertificate) { Owner = this });

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
                () => OpenAddMachineWindow(existing: null), OpenGiveAccessWindow, OpenJoinClassWindow) { Owner = this };
            window.Closed += (_, _) => _machinesWindow = null;
            _machinesWindow = window;
            OwnedWindow.ReturnToOwnerWhenClosed(window);
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

    // "Dar acesso a um colega": one invite at a time, so asking again brings
    // the open one back.
    private void OpenGiveAccessWindow() =>
        ShowOnce("convite", Array.Empty<KnownMachine>(),
            () => new GiveAccessWindow(_masterCertificate, () => _tiles.Select(tile => tile.Machine).ToList()) { Owner = this });

    private void OpenJoinClassWindow() =>
        ShowOnce("entrar-na-sala", Array.Empty<KnownMachine>(),
            () => new JoinClassWindow(_masterCertificate, AddMachines) { Owner = this });

    private void OnJoinClassClicked(object sender, RoutedEventArgs e) => OpenJoinClassWindow();

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

        // Renamed ("Editar a máquina"): its tile stays where the teacher put it.
        if (original is not null && !string.Equals(original.Name, updated.Name, StringComparison.Ordinal))
        {
            TileArrangement.Renamed(_cells, original.Name, updated.Name);
            SaveCells();
        }
        InsertTile(position, updated);
        UpdateEmptyHint();
        SaveRoster();
    }

    // Called by MachinesWindow when a pairing is confirmed (see Roster.Place).
    // The grid changes first; a failure to write the list then propagates
    // for MachinesWindow to report.
    private void AddPairedMachine(KnownMachine machine) => AddMachines(new[] { machine });

    // Also called by JoinClassWindow with a colleague's class: each machine
    // placed as a paired one (Roster.Place, in the colleague's order), then
    // the list saved once. A machine already in the list exactly as given
    // keeps its tile: replacing it would give a locked screen back.
    private void AddMachines(IReadOnlyList<KnownMachine> machines)
    {
        foreach (var machine in machines)
        {
            var roster = _tiles.Select(tile => tile.Machine).ToList();
            var placed = Roster.Place(roster, machine);
            if (!Roster.Changes(roster, placed))
            {
                continue;
            }

            if (placed.Replaces)
            {
                RemoveTileAt(placed.Index);
            }
            InsertTile(placed.Index, placed.Machine);
        }
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
        // Where the teacher placed it in the room, if ever ("Organizar sala").
        if (_cells.TryGetValue(machine.Name, out var cell))
        {
            tile.Cell = cell;
        }
        // A machine "Só online" hides can still change what the grid says
        // when it shows none (still looking, or none online).
        tile.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MachineTile.Presence))
            {
                PublishPresence(tile);
                UpdateEmptyHint();
            }
        };
        _tiles.Insert(index, tile);
        PublishPresence(tile);
        PublishRoster();
        // A machine new to a room the teacher organized: the first free cell.
        if (_arranged && RowWidth() > 0)
        {
            PlaceTiles();
        }

        var poller = new ThumbnailPoller(
            machine, _masterCertificate,
            powerSchedule: () => Volatile.Read(ref _powerSchedule),
            appBlockPolicy: () => Volatile.Read(ref _appBlockPolicy),
            thumbnailSize: () => TileSize.Request(Volatile.Read(ref _drawnTileWidth), Volatile.Read(ref _dpiScale)),
            classLesson: LessonForPollers, classHomeClaim: StragglerClaim, classLessonOver: EndOfLessonOver,
            classLessonHeldEnd: IsHeldEnd);
        // Decoded on the poller's own thread (frozen, so the UI thread may
        // use it); only the swap onto the tile happens on the UI thread.
        poller.ThumbnailReceived += jpeg =>
        {
            var image = DecodeThumbnail(jpeg);
            Dispatcher.InvokeAsync(() =>
            {
                if (image is null)
                {
                    tile.ShowScreenUnavailable();
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
        var wasSelected = tile.IsSelected;
        UnlockScreen(tile);
        _tiles.RemoveAt(index);
        PublishRoster();
        // Another entry for the same machine (a hand-edited list) keeps it known.
        if (_tiles.FirstOrDefault(other => string.Equals(
                other.Machine.SessionThumbprint, tile.Machine.SessionThumbprint, StringComparison.OrdinalIgnoreCase)) is { } same)
        {
            PublishPresence(same);
        }
        else
        {
            _online.TryRemove(tile.Machine.SessionThumbprint, out _);
        }
        // Out of the selection too; the buttons and status bar only change if
        // it was in it (a replaced or moved machine is a new tile).
        _selection.Prune();
        if (wasSelected)
        {
            RefreshSelection();
        }
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

    // No machines at all, or "Só online" hiding every one of them: said in
    // the grid instead of leaving it blank.
    private void UpdateEmptyHint()
    {
        EmptyRosterPanel.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var notice = GridFilter.EmptyNotice(_tiles, _onlineOnly);
        NoneShownText.Text = notice ?? string.Empty;
        NoneShownPanel.Visibility = notice is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        // A size still settling is saved now.
        if (_saveTimer.IsEnabled)
        {
            SaveGridViewNow();
        }

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

        // The lesson goes on: the next Master to open carries on with it. The
        // host closes first, so no colleague's command reaches a sync stopping.
        _classRole.Dispose();
        _classSync.Dispose();
    }
}
