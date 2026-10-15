using System.Windows;

namespace Midview.Master;

// The selection rectangle dragged over the classroom grid's empty space, as
// plain geometry: MainWindow gathers each tile's bounds and the mouse
// positions (all on the same surface, so scrolling moves them together) and
// hands the tiles this returns to GridSelection.Band.
public static class RubberBand
{
    // How far the mouse must move, across or down, before a press on empty
    // space is a drag and not a click (which clears the selection instead).
    public const double DragThreshold = 4;

    public static bool IsDrag(Point start, Point current) =>
        Math.Abs(current.X - start.X) > DragThreshold || Math.Abs(current.Y - start.Y) > DragThreshold;

    // Whichever way it was dragged, up and left or down and right.
    public static Rect Between(Point start, Point current) => new(start, current);

    // Every tile the rectangle touches, even by an edge or a corner, in the
    // order given (the grid's). A tile with no bounds (not laid out) is skipped.
    public static IReadOnlyList<T> Touched<T>(Rect band, IEnumerable<(T Item, Rect Bounds)> tiles) =>
        tiles.Where(tile => !tile.Bounds.IsEmpty && band.IntersectsWith(tile.Bounds))
            .Select(tile => tile.Item)
            .ToList();
}
