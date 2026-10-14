namespace Midview.Master;

// The classroom grid's current multi-selection, kept apart from the WPF
// controls so the click rules (plain, Ctrl, Shift) can be reasoned about and
// tested on their own. It reads the live, ordered item list it's given, so a
// tile added or removed from the grid is reflected without rebuilding;
// Selected is always returned in grid order. Anchor is the tile Shift extends
// from and the one "Ver a tela" / "Controlar" act on.
public sealed class GridSelection<T> where T : class
{
    private static readonly EqualityComparer<T> Same = EqualityComparer<T>.Default;

    private readonly IReadOnlyList<T> _items;
    private readonly HashSet<T> _selected = new();
    private T? _anchor;

    public GridSelection(IReadOnlyList<T> itemsInGridOrder)
    {
        _items = itemsInGridOrder;
    }

    public T? Anchor => _anchor;

    public IReadOnlyList<T> Selected => _items.Where(_selected.Contains).ToList();

    // Plain click: this one alone, and it becomes the anchor.
    public void Click(T item)
    {
        _selected.Clear();
        _selected.Add(item);
        _anchor = item;
    }

    // Ctrl+click: add or remove this one, leaving the rest. Adding makes it the
    // anchor; removing the current anchor re-points it at whatever's still
    // selected (the last in grid order), or nothing.
    public void CtrlClick(T item)
    {
        if (_selected.Remove(item))
        {
            if (Same.Equals(_anchor, item))
            {
                _anchor = _items.LastOrDefault(_selected.Contains);
            }
        }
        else
        {
            _selected.Add(item);
            _anchor = item;
        }
    }

    // Shift+click: the run from the anchor to here, in grid order, replacing the
    // selection. With no anchor (or an anchor no longer in the grid) it's just a
    // plain click. The anchor itself doesn't move.
    public void ShiftClick(T item)
    {
        var anchorIndex = _anchor is null ? -1 : IndexOf(_anchor);
        var targetIndex = IndexOf(item);
        if (anchorIndex < 0 || targetIndex < 0)
        {
            Click(item);
            return;
        }

        var low = Math.Min(anchorIndex, targetIndex);
        var high = Math.Max(anchorIndex, targetIndex);
        _selected.Clear();
        for (var i = low; i <= high; i++)
        {
            _selected.Add(_items[i]);
        }
    }

    // Drop anything no longer in the grid (a machine that left the roster), and
    // re-point the anchor if it was the one that left.
    public void Prune()
    {
        _selected.RemoveWhere(item => IndexOf(item) < 0);
        if (_anchor is not null && IndexOf(_anchor) < 0)
        {
            _anchor = _items.LastOrDefault(_selected.Contains);
        }
    }

    // Nothing selected, no anchor -- e.g. after a click on empty space.
    public void Clear()
    {
        _selected.Clear();
        _anchor = null;
    }

    private int IndexOf(T item)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (Same.Equals(_items[i], item))
            {
                return i;
            }
        }
        return -1;
    }
}
