using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Midview.Protocol;

namespace Midview.Master;

// The classroom grid's layout: how big the tiles are (TileSize),
// "Visualizar todos" keeping every machine shown in view, and the tiles
// placed like the room ("Organizar sala", TileArrangement), all remembered
// in GridViewFile and TileCellsFile.
public partial class MainWindow
{
    // The size the teacher chose (the slider, remembered).
    private int _tileWidth = TileSize.Default;

    // How wide the tiles' screens are drawn (in rows, wider than the size
    // chosen, to fill the row: TileSize.ToFillRow) and Windows' scale on
    // this window's monitor: the pollers read both on their own threads to
    // ask each machine for a thumbnail that size.
    private int _drawnTileWidth = TileSize.Default;
    private double _dpiScale = 1.0;

    // What a tile adds across a row around its screen: the card's border (1
    // on each side) and the tile's margin (4), App.xaml's MachineTileButton.
    private const double TileChromeWidth = 10;

    // "Visualizar todos" switched on: every machine shown stays in view, the
    // tiles resized again (FitSoon) whenever that could change -- the window,
    // the machines shown, the room.
    private bool _fitAll;
    private readonly DispatcherTimer _fitTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    // A size dragged along the slider is saved once it settles, not at every
    // pixel of the drag.
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    // "Organizar sala": the tiles at the places the teacher dragged them to,
    // by machine name (TileCellsFile), and the panel that lays them out.
    private bool _arranged;
    private Dictionary<string, TileCell> _cells = new(StringComparer.OrdinalIgnoreCase);
    private TileGridPanel? _tilePanel;

    // A tile pressed while "Organizar sala" is on, which a move turns into a
    // drag: where the press was (on GridSurface), and once dragging, every
    // tile moving with it (the selection it is part of) and where each began.
    private Point? _dragStart;
    private MachineTile? _dragTile;
    private List<(MachineTile Tile, TileCell From)>? _dragging;
    private int _dragZIndex;

    // Wheel turns not yet worth a whole step (a touchpad sends small ones).
    private int _wheelDelta;

    private void LoadTileLayout(GridView view)
    {
        TileSizeSlider.Minimum = TileSize.Smallest;
        TileSizeSlider.Maximum = TileSize.Largest;
        _fitTimer.Tick += (_, _) =>
        {
            _fitTimer.Stop();
            FitAll();
        };
        _saveTimer.Tick += (_, _) => SaveGridViewNow();

        _cells = TileCellsFile.Load(TileCellsFile.DefaultPath);
        _arranged = view.Arranged;
        ArrangeButton.IsChecked = _arranged;
        AlignButton.Visibility = _arranged ? Visibility.Visible : Visibility.Collapsed;
        _fitAll = view.FitAll;
        FitAllButton.IsChecked = _fitAll;
        ShowTilesAt(view.TileWidth);
        // Only now: setting the slider's range above moved it, which isn't
        // the teacher choosing a size.
        TileSizeSlider.ValueChanged += OnTileSizeSliderChanged;
    }

    // Ctrl +, Ctrl - and Ctrl 0, on the main keys or the number pad, as a
    // browser zooms.
    private int? TileWidthForKey(Key key) => key switch
    {
        Key.OemPlus or Key.Add => LargerTiles(_tileWidth),
        Key.OemMinus or Key.Subtract => SmallerTiles(_tileWidth),
        Key.D0 or Key.NumPad0 => TileSize.Default,
        _ => null,
    };

    // Ctrl + the wheel resizes the tiles instead of scrolling the grid.
    private void OnWindowPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }
        _wheelDelta += e.Delta;
        var width = _tileWidth;
        for (; _wheelDelta >= Mouse.MouseWheelDeltaForOneLine; _wheelDelta -= Mouse.MouseWheelDeltaForOneLine)
        {
            width = LargerTiles(width);
        }
        for (; _wheelDelta <= -Mouse.MouseWheelDeltaForOneLine; _wheelDelta += Mouse.MouseWheelDeltaForOneLine)
        {
            width = SmallerTiles(width);
        }
        ResizeTiles(width);
        e.Handled = true;
    }

    private void OnTileSizeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        ResizeTiles((int)e.NewValue);

    private void OnSmallerTilesClicked(object sender, RoutedEventArgs e) => ResizeTiles(SmallerTiles(_tileWidth));

    private void OnLargerTilesClicked(object sender, RoutedEventArgs e) => ResizeTiles(LargerTiles(_tileWidth));

    // One step that changes the tiles on screen: in rows, one tile fewer or
    // more across; placed like the room, an eighth bigger or smaller.
    private int LargerTiles(int width) => TileSize.Larger(width, _arranged ? 0 : RowWidth(), TileChromeWidth);

    private int SmallerTiles(int width) => TileSize.Smaller(width, _arranged ? 0 : RowWidth(), TileChromeWidth);

    // The width the grid's rows have for tiles: inside the grid's margin, a
    // pixel short so rounding never wraps a last tile onto a row of its own,
    // and less the vertical scroll bar even while it is hidden -- tiles
    // widened into its place would make the grid taller and bring it back.
    private double RowWidth()
    {
        var margin = MachinesItemsControl.Margin;
        return MachinesScrollViewer.ActualWidth - SystemParameters.VerticalScrollBarWidth - margin.Left - margin.Right - 1;
    }

    // The teacher chose a size: shown now, and the next time Midview opens.
    // Choosing one by hand lets go of "Visualizar todos".
    private void ResizeTiles(int width)
    {
        width = TileSize.Clamp(width);
        if (width == _tileWidth)
        {
            return;
        }
        if (_fitAll)
        {
            SetFitAll(false);
        }
        ShowTilesAt(width);
        SaveGridView();
    }

    // Every tile's screen at this width (MainWindow.xaml's TileWidth and
    // TileHeight); each machine is asked for a thumbnail to match from its
    // next poll on, and the slider follows.
    private void ShowTilesAt(int width)
    {
        _tileWidth = width;
        TileSizeSlider.Value = width;
        FillRows();
    }

    // In rows, the tiles widened to fill them at the size chosen, again
    // whenever the window, and with it the rows, changes width; placed like
    // the room, just the size chosen.
    private void FillRows()
    {
        var rowWidth = RowWidth();
        var drawn = _arranged ? _tileWidth : TileSize.ToFillRow(_tileWidth, rowWidth, TileChromeWidth);
        Volatile.Write(ref _drawnTileWidth, drawn);
        Resources["TileWidth"] = (double)drawn;
        Resources["TileHeight"] = (double)TileSize.HeightOf(drawn);
        if (_arranged && rowWidth > 0)
        {
            PlaceTiles();
        }
    }

    private void OnMachinesScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            FillRows();
        }
        FitSoon();
    }

    // A badge showing up under a tile makes its row taller.
    private void OnMachinesItemsSizeChanged(object sender, SizeChangedEventArgs e) => FitSoon();

    // Thumbnails stay sharp on a monitor with another scale.
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Volatile.Write(ref _dpiScale, newDpi.DpiScaleX);
    }

    private void SaveGridView()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveGridViewNow()
    {
        _saveTimer.Stop();
        try
        {
            GridViewFile.Save(GridViewFile.DefaultPath, new GridView(_tileWidth, _onlineOnly, _fitAll, _arranged));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = $"O tamanho e o filtro das telas não puderam ser guardados: {ErrorText.Describe(ex)}";
        }
    }

    // ---- "Visualizar todos" ----

    private void OnFitAllClicked(object sender, RoutedEventArgs e) => SetFitAll(FitAllButton.IsChecked == true);

    private void SetFitAll(bool fitAll)
    {
        FitAllButton.IsChecked = fitAll;
        if (fitAll == _fitAll)
        {
            return;
        }
        _fitAll = fitAll;
        SaveGridView();
        FitSoon();
    }

    // A moment later, once the grid has settled (and only once for several
    // changes at a time).
    private void FitSoon()
    {
        if (_fitAll)
        {
            _fitTimer.Stop();
            _fitTimer.Start();
        }
    }

    // The largest size at which every machine shown is in view without
    // scrolling: in rows, the fewest across whose rows fit the height
    // (TileSize.ToFitAll); placed like the room, the whole room
    // (TileSize.ToFitRoom).
    private void FitAll()
    {
        if (!_fitAll)
        {
            return;
        }
        var shown = _shownTiles.Cast<MachineTile>().ToList();
        var containers = shown
            .Select(tile => MachinesItemsControl.ItemContainerGenerator.ContainerFromItem(tile))
            .OfType<FrameworkElement>()
            .ToList();
        if (containers.Count == 0)
        {
            return;
        }

        // What a tile adds around its screen, as drawn now: the card, its
        // margin, and the name and badges (the tallest, since a row is as
        // tall as its tallest tile). Not laid out yet: once it is.
        var chromeHeight = containers.Max(container => container.ActualHeight) - TileSize.HeightOf(_drawnTileWidth);
        if (chromeHeight < 0)
        {
            FitSoon();
            return;
        }
        // Inside the grid's margin, and a pixel short so rounding never
        // wraps a last tile onto a row of its own.
        var margin = MachinesItemsControl.Margin;
        var availableHeight = MachinesScrollViewer.ActualHeight - margin.Top - margin.Bottom - 1;

        int width;
        if (_arranged)
        {
            var (columns, rows) = TileArrangement.Extent(shown.Select(tile => tile.Cell ?? new TileCell(0, 0)));
            width = TileSize.ToFitRoom(columns, rows, RowWidth(), availableHeight, TileChromeWidth, chromeHeight);
        }
        else
        {
            width = TileSize.ToFitAll(containers.Count, RowWidth(), availableHeight, TileChromeWidth, chromeHeight);
        }
        if (width != _tileWidth)
        {
            ShowTilesAt(width);
            SaveGridView();
        }
    }

    // ---- "Organizar sala" ----

    private void OnTilePanelLoaded(object sender, RoutedEventArgs e)
    {
        _tilePanel = (TileGridPanel)sender;
        _tilePanel.Arranged = _arranged;
    }

    private void OnArrangeClicked(object sender, RoutedEventArgs e)
    {
        _arranged = ArrangeButton.IsChecked == true;
        AlignButton.Visibility = _arranged ? Visibility.Visible : Visibility.Collapsed;
        if (_tilePanel is not null)
        {
            _tilePanel.Arranged = _arranged;
        }
        // Out of the rows, the tiles keep the size they were drawn at there.
        if (_arranged)
        {
            _tileWidth = _drawnTileWidth;
            TileSizeSlider.Value = _tileWidth;
        }
        FillRows();
        SaveGridView();
        FitSoon();
        StatusTextBlock.Text = _arranged
            ? "Arraste as telas para ficarem como as máquinas estão na sala. Arrastar uma tela selecionada leva junto as outras selecionadas."
            : string.Empty;
    }

    private void OnAlignClicked(object sender, RoutedEventArgs e)
    {
        var placed = _tiles.Where(tile => tile.Cell is not null).ToList();
        var aligned = TileArrangement.Aligned(placed.Select(tile => tile.Cell!.Value).ToList());
        for (var i = 0; i < placed.Count; i++)
        {
            placed[i].Cell = aligned[i];
        }
        SaveCells();
        FitSoon();
    }

    // Every tile a place: the one it had, or the first time the room is
    // organized, where the rows show it now; a machine new to the room, the
    // first free cell.
    private void PlaceTiles()
    {
        var changed = false;
        var columns = TileSize.Columns(_drawnTileWidth, RowWidth(), TileChromeWidth);
        if (_cells.Count == 0 && _tiles.All(tile => tile.Cell is null))
        {
            var index = 0;
            foreach (var tile in _shownTiles.Cast<MachineTile>())
            {
                tile.Cell = TileArrangement.FromRows(index++, columns);
            }
            changed = true;
        }
        foreach (var tile in _tiles.Where(tile => tile.Cell is null))
        {
            tile.Cell = _cells.TryGetValue(tile.Name, out var saved)
                ? saved
                : TileArrangement.FirstFree(_tiles.Select(other => other.Cell).OfType<TileCell>().ToList(), columns);
            changed = true;
        }
        if (changed)
        {
            SaveCells();
        }
    }

    private void SaveCells()
    {
        foreach (var tile in _tiles)
        {
            if (tile.Cell is { } cell)
            {
                _cells[tile.Name] = cell;
            }
        }
        try
        {
            TileCellsFile.Save(TileCellsFile.DefaultPath, _cells);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = $"A posição das telas não pôde ser guardada: {ErrorText.Describe(ex)}";
        }
    }

    // A press on a tile, which still selects it on release unless the mouse
    // moves far enough first to drag it (OnGridSurfacePreviewMouseMove).
    private void OnTilePressed(object sender, MouseButtonEventArgs e)
    {
        if (_arranged && ((FrameworkElement)sender).DataContext is MachineTile tile)
        {
            _dragStart = e.GetPosition(GridSurface);
            _dragTile = tile;
        }
    }

    private void OnGridSurfacePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _dragTile is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var current = e.GetPosition(GridSurface);
        if (_dragging is null)
        {
            if (!RubberBand.IsDrag(start, current))
            {
                return;
            }
            BeginDrag();
        }
        var cell = _tilePanel?.CellSize ?? Size.Empty;
        if (_dragging is null || cell.Width <= 0 || cell.Height <= 0)
        {
            return;
        }
        var (dx, dy) = ((current.X - start.X) / cell.Width, (current.Y - start.Y) / cell.Height);
        foreach (var (tile, from) in _dragging)
        {
            tile.Cell = TileArrangement.Dropped(new TileCell(from.X + dx, from.Y + dy));
        }
        e.Handled = true;
    }

    // The tile pressed, with the rest of the selection when it is part of
    // one; drawn over the others while it moves, and after, so one let go
    // over another stays in sight until "Alinhar". Taking the mouse from the
    // tile's button keeps the release from clicking it.
    private void BeginDrag()
    {
        var moving = _dragTile!.IsSelected && _selection.Selected.Count > 1
            ? _selection.Selected.ToList()
            : new List<MachineTile> { _dragTile };
        _dragging = moving.Select(tile => (tile, tile.Cell ?? new TileCell(0, 0))).ToList();
        _dragZIndex++;
        foreach (var tile in moving)
        {
            if (MachinesItemsControl.ItemContainerGenerator.ContainerFromItem(tile) is UIElement container)
            {
                Panel.SetZIndex(container, _dragZIndex);
            }
        }
        GridSurface.CaptureMouse();
    }

    private void OnGridSurfacePreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is not null)
        {
            EndDrag();
            GridSurface.ReleaseMouseCapture();
            e.Handled = true;
        }
        _dragStart = null;
        _dragTile = null;
    }

    // Released, or the mouse taken away (Esc, another window): the tiles
    // stay where they were let go, and the room is saved.
    private void EndDrag()
    {
        if (_dragging is null)
        {
            return;
        }
        _dragging = null;
        _dragStart = null;
        _dragTile = null;
        SaveCells();
        FitSoon();
    }
}
