namespace Midview.Master;

// The teacher's side of the class chat: the one-machine chat (ChatConnection)
// opened on every machine at once, so each student gets the usual "Conversa
// com o professor" window. The teacher's lines go to every machine still in
// the conversation; a student's reply comes back to the teacher alone, tagged
// with the machine's name, and is never passed on to the other students. A
// machine that leaves is dropped. Dispose ends the conversation everywhere.
public sealed class ClassChat : IDisposable
{
    private readonly IReadOnlyList<KnownMachine> _machines;
    private readonly Func<KnownMachine, IChatConnection> _open;
    private readonly object _gate = new();

    // Each machine is in exactly one of these once it answered: still in the
    // conversation, couldn't connect (and why), or left after joining.
    private readonly Dictionary<IChatConnection, string> _connected = new();
    private readonly List<(string Name, string Reason)> _notConnected = new();
    private readonly List<string> _left = new();

    // The last line sent: each line waits for the one before it, so two typed
    // quickly reach every machine in the order they were typed.
    private Task _sending = Task.CompletedTask;
    private bool _disposed;

    public ClassChat(IReadOnlyList<KnownMachine> machines, Func<KnownMachine, IChatConnection> open)
    {
        _machines = machines;
        _open = open;
    }

    // Both raised on a background thread. LineReceived is a student's reply,
    // as "Aluno-03: terminei"; Changed means Status has something new.
    public event Action<string>? LineReceived;
    public event Action? Changed;

    public int ConnectedCount
    {
        get
        {
            lock (_gate)
            {
                return _connected.Count;
            }
        }
    }

    // "Conectado a 18 de 20 máquinas.", then whoever is still being waited
    // for, couldn't connect or left.
    public string Status
    {
        get
        {
            lock (_gate)
            {
                var total = _machines.Count;
                var machines = total == 1 ? "máquina" : "máquinas";
                var waiting = total - _connected.Count - _notConnected.Count - _left.Count;
                if (waiting == total)
                {
                    return $"Conectando a {total} {machines}...";
                }

                var notConnected = _notConnected.OrderBy(machine => machine.Name, NaturalOrder.Instance)
                    .Select(machine => $"{machine.Name} ({machine.Reason})");
                return $"Conectado a {_connected.Count} de {total} {machines}."
                    + (waiting > 0 ? $" Aguardando {waiting}." : string.Empty)
                    + (_notConnected.Count > 0 ? $" Não conectou: {string.Join(", ", notConnected)}." : string.Empty)
                    + (_left.Count > 0 ? $" Saíram: {string.Join(", ", _left.Order(NaturalOrder.Instance))}." : string.Empty)
                    + (waiting == 0 && _connected.Count == 0 ? " Feche e abra de novo para reconectar." : string.Empty);
            }
        }
    }

    // All machines at once: an offline one, which takes a while to time out,
    // must not hold up the rest.
    public Task ConnectAsync() => Task.WhenAll(_machines.Select(machine => Task.Run(() => Connect(machine))));

    // Returns how many machines the line reached. One it didn't reach has
    // left the conversation, and shows so in Status.
    public Task<int> SendAsync(string text)
    {
        lock (_gate)
        {
            var send = _sending.ContinueWith(_ => SendToEveryone(text), TaskScheduler.Default).Unwrap();
            _sending = send;
            return send;
        }
    }

    public void Dispose()
    {
        IChatConnection[] connections;
        lock (_gate)
        {
            _disposed = true;
            connections = _connected.Keys.ToArray();
            _connected.Clear();
        }
        // Outside the lock: each one raises Closed as it goes.
        foreach (var connection in connections)
        {
            connection.Dispose();
        }
    }

    private void Connect(KnownMachine machine)
    {
        IChatConnection connection;
        try
        {
            connection = _open(machine);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _notConnected.Add((machine.Name, FailureText.Describe(ex)));
            }
            Changed?.Invoke();
            return;
        }

        connection.StudentMessageReceived += text => LineReceived?.Invoke($"{machine.Name}: {text}");
        connection.Closed += _ => OnLeft(connection);
        bool joined;
        lock (_gate)
        {
            joined = !_disposed;
            if (joined)
            {
                _connected.Add(connection, machine.Name);
                // Under the lock, so Dispose can't close it in between.
                connection.Start();
            }
        }

        if (!joined)
        {
            // The conversation ended while this machine was still connecting.
            connection.Dispose();
            return;
        }
        Changed?.Invoke();
    }

    private void OnLeft(IChatConnection connection)
    {
        lock (_gate)
        {
            // Not there once Dispose took it out: the teacher ending the
            // conversation isn't the machine leaving.
            if (!_connected.Remove(connection, out var name))
            {
                return;
            }
            _left.Add(name);
        }
        Changed?.Invoke();
    }

    private async Task<int> SendToEveryone(string text)
    {
        IChatConnection[] connections;
        lock (_gate)
        {
            connections = _connected.Keys.ToArray();
        }
        // In parallel, like ConnectAsync: a dropping connection can take a
        // while to fail.
        var reached = await Task.WhenAll(connections.Select(connection => Task.Run(() => connection.Send(text))));
        return reached.Count(sent => sent);
    }
}
