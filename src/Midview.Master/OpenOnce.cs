using System.Diagnostics.CodeAnalysis;

namespace Midview.Master;

// One window per action and machines: the toolbar's "Mensagem" clicked three
// times is one window, the same for ALUNO-03 alone is another. Asking again
// finds the one open instead of making a copy. Generic, so the rule is
// tested without windows.
public sealed class OpenOnce<T> where T : class
{
    private readonly Dictionary<string, T> _open = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _opening = new(StringComparer.OrdinalIgnoreCase);

    public static string Key(string action, IEnumerable<string> machineNames) =>
        action + ":" + string.Join("|", machineNames.Order(StringComparer.OrdinalIgnoreCase));

    public bool TryGet(string key, [NotNullWhen(true)] out T? item) => _open.TryGetValue(key, out item);

    // For one that takes a while to make (it connects first): false while it
    // is being made or is open, so a second click meanwhile makes nothing.
    public bool TryStartOpening(string key) => !_open.ContainsKey(key) && _opening.Add(key);

    // It couldn't be made after all (the connection failed).
    public void StopOpening(string key) => _opening.Remove(key);

    public void Add(string key, T item)
    {
        _opening.Remove(key);
        _open[key] = item;
    }

    // Only if it's still that one: a newer one may have the key by now.
    public void Remove(string key, T item)
    {
        if (_open.TryGetValue(key, out var current) && ReferenceEquals(current, item))
        {
            _open.Remove(key);
        }
    }
}
