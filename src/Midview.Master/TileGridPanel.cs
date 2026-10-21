using System.Windows;
using System.Windows.Controls;

namespace Midview.Master;

// The classroom grid's layout. In rows, like a WrapPanel: left to right,
// a new row when one is full, each row as tall as its tallest tile. Placed
// like the room (Arranged), each tile at its Cell, one cell being as big as
// the biggest tile, so the room keeps its shape at any tile size.
public sealed class TileGridPanel : Panel
{
    public static readonly DependencyProperty ArrangedProperty = DependencyProperty.Register(
        nameof(Arranged), typeof(bool), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    // Set on each tile's container from MachineTile.Cell (MainWindow.xaml).
    public static readonly DependencyProperty CellProperty = DependencyProperty.RegisterAttached(
        "Cell", typeof(TileCell?), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static TileCell? GetCell(DependencyObject element) => (TileCell?)element.GetValue(CellProperty);

    public static void SetCell(DependencyObject element, TileCell? value) => element.SetValue(CellProperty, value);

    public bool Arranged
    {
        get => (bool)GetValue(ArrangedProperty);
        set => SetValue(ArrangedProperty, value);
    }

    // One cell of the room, as last laid out: what a drag moves a tile by.
    public Size CellSize { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var cell = new Size();
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            cell.Width = Math.Max(cell.Width, child.DesiredSize.Width);
            cell.Height = Math.Max(cell.Height, child.DesiredSize.Height);
        }
        CellSize = cell;
        return Arranged ? RoomSize() : LayOutRows(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!Arranged)
        {
            LayOutRows(finalSize.Width, arrange: true);
            return finalSize;
        }
        foreach (UIElement child in InternalChildren)
        {
            var cell = GetCell(child) ?? new TileCell(0, 0);
            child.Arrange(new Rect(
                new Point(cell.X * CellSize.Width, cell.Y * CellSize.Height), child.DesiredSize));
        }
        return finalSize;
    }

    private Size RoomSize()
    {
        var extent = TileArrangement.Extent(
            InternalChildren.Cast<UIElement>().Select(child => GetCell(child) ?? new TileCell(0, 0)));
        return InternalChildren.Count == 0
            ? new Size()
            : new Size(extent.Columns * CellSize.Width, extent.Rows * CellSize.Height);
    }

    // The rows: measured (arrange false) or placed. Returns the size they take.
    private Size LayOutRows(double width, bool arrange)
    {
        double x = 0, y = 0, rowHeight = 0, widest = 0;
        var row = new List<UIElement>();
        foreach (UIElement child in InternalChildren)
        {
            var size = child.DesiredSize;
            if (row.Count > 0 && x + size.Width > width)
            {
                PlaceRow(row, y, rowHeight, arrange);
                y += rowHeight;
                x = 0;
                rowHeight = 0;
                row.Clear();
            }
            row.Add(child);
            x += size.Width;
            widest = Math.Max(widest, x);
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        PlaceRow(row, y, rowHeight, arrange);
        return new Size(widest, y + rowHeight);
    }

    private static void PlaceRow(List<UIElement> row, double y, double height, bool arrange)
    {
        if (!arrange)
        {
            return;
        }
        double x = 0;
        foreach (var child in row)
        {
            child.Arrange(new Rect(x, y, child.DesiredSize.Width, height));
            x += child.DesiredSize.Width;
        }
    }
}
