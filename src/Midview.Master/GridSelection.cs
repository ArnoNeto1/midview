namespace Midview.Master;

// The classroom grid's current multi-selection, kept apart from the WPF
// controls so the click rules (plain, Ctrl, Shift) can be reasoned about and
// tested on their own. It reads the live, ordered item list it's given, so a
// tile added or removed from the grid is reflected without rebuilding;
// Selected is always returned in grid order. Anchor is the tile Shift extends
// from. "Ver a tela" / "Controlar" act on the selection only while it is one
// tile; a tile's menu acts on TargetsOf that tile. A drag over the grid's
// empty space selects what its rectangle touches (BeginBand, Band).
public sealed class GridSelection<T> where T : class
{
    private static readonly EqualityComparer<T> Same = EqualityComparer<T>.Default;

    private readonly IReadOnlyList<T> _items;
    private readonly HashSet<T> _selected = new();
    private T? _anchor;

    // The selection and anchor when the current rectangle started (BeginBand).
    private IReadOnlyList<T> _bandBase = Array.Empty<T>();
    private T? _bandBaseAnchor;

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

    // A press on the grid's empty space, which may become a selection
    // rectangle: remembers the selection as it is, for a Ctrl-drag to add to.
    public void BeginBand()
    {
        _bandBase = _items.Where(_selected.Contains).ToList();
        _bandBaseAnchor = _anchor;
    }

    // While dragging: the tiles the rectangle touches now -- alone, or (add,
    // Ctrl held at the start) with the selection there was at BeginBand. Each
    // move recomputes it from that start, so a tile the rectangle leaves again
    // is let go. The anchor, for a later Shift+click, is the first touched tile
    // in grid order; touching none leaves the anchor the start had (Ctrl) or
    // none.
    public void Band(IEnumerable<T> touched, bool add)
    {
        var inGrid = new HashSet<T>(touched.Where(item => IndexOf(item) >= 0));
        _selected.Clear();
        if (add)
        {
            _selected.UnionWith(_bandBase.Where(item => IndexOf(item) >= 0));
        }
        _selected.UnionWith(inGrid);

        _anchor = _items.FirstOrDefault(inGrid.Contains)
            ?? (add && _bandBaseAnchor is not null && _selected.Contains(_bandBaseAnchor) ? _bandBaseAnchor : null);
    }

    // Right-click: a tile that's part of a selection of several keeps that
    // selection, for its menu to act on all of them; any other tile becomes
    // the only one, as with a plain click.
    public void RightClick(T item)
    {
        if (TargetsOf(item).Count > 1)
        {
            return;
        }
        Click(item);
    }

    // What a tile's menu acts on: the whole selection, in grid order, when the
    // tile is one of several selected; otherwise just that tile.
    public IReadOnlyList<T> TargetsOf(T item)
    {
        var selected = Selected;
        return selected.Count > 1 && selected.Contains(item) ? selected : new[] { item };
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
