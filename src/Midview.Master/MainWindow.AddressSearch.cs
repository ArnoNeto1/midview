using System.Windows.Threading;
using Midview.Protocol;

namespace Midview.Master;

// Machines of the room found at a new address: by "Procurar na rede"
// (MachinesWindow calls RefreshAddressesAsync), and by the Master itself while
// a machine may have moved (AddressSearchSchedule, AddressCheck.MayHaveMoved).
// Both use the same search (DiscoveryClient), the same matching by Agent
// (Roster.NewAddresses) and the same proof before moving (AddressCheck):
// only machines already in the list move; none is ever added or paired.
// The Master's own search is quiet: no window, no status line, and nothing
// it runs into stops the Master.
public partial class MainWindow : ITileMoveHost
{
    // How often the Master checks whether a search is due.
    private static readonly TimeSpan AddressSearchCheck = TimeSpan.FromSeconds(5);

    private readonly AddressSearchSchedule _addressSearch = new(DateTime.UtcNow);
    private DispatcherTimer? _addressSearchTimer;
    private bool _addressSearching;

    // Started by App once the classroom is open, so a window made elsewhere
    // (a test, a preview) never searches the network by itself.
    public void StartAddressSearch()
    {
        if (_addressSearchTimer is not null)
        {
            return;
        }
        _addressSearchTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = AddressSearchCheck };
        _addressSearchTimer.Tick += OnAddressSearchTick;
        _addressSearchTimer.Start();
    }

    private void StopAddressSearch()
    {
        _addressSearchTimer?.Stop();
        _addressSearchTimer = null;
    }

    private async void OnAddressSearchTick(object? sender, EventArgs e)
    {
        try
        {
            await SearchAddressesIfDueAsync();
        }
        catch (Exception ex)
        {
            // A search nobody asked for never ends the class: tried again later.
            System.Diagnostics.Trace.WriteLine("MainWindow: procura automática falhou: " + ex);
        }
    }

    private bool MayHaveMoved(MachineTile tile) => AddressCheck.MayHaveMoved(tile.Machine, tile.Presence, tile.Status);

    private async Task SearchAddressesIfDueAsync()
    {
        var lookedFor = _tiles.Where(MayHaveMoved).Select(tile => tile.Machine.AgentThumbprint!);
        if (!_addressSearch.Due(DateTime.UtcNow, lookedFor) || _addressSearching)
        {
            return;
        }

        _addressSearching = true;
        var moved = 0;
        try
        {
            var found = await Task.Run(SearchNetwork);
            if (_addressSearchTimer is null)
            {
                return; // the Master closed meanwhile
            }

            // The list as it is now: the teacher may have changed it meanwhile.
            var roster = _tiles.Select(tile => tile.Machine).ToList();
            var changes = Roster.NewAddressesWhere(roster, found, index => MayHaveMoved(_tiles[index]));
            var proven = await AddressCheck.Proven(WithOld(roster, changes), AgentAnswers);
            if (_addressSearchTimer is null)
            {
                return;
            }
            // Only the ones still looked for: a machine back at its address stays.
            moved = Move(proven.Where(move => _tiles.FirstOrDefault(tile => tile.Machine.IsSameEntryAs(move.Old)) is { } tile
                && MayHaveMoved(tile)).ToList());
        }
        finally
        {
            _addressSearching = false;
            _addressSearch.Searched(DateTime.UtcNow, moved);
        }
    }

    // Called by MachinesWindow after a search: machines found at another
    // address (see Roster.NewAddresses) and proven there. Returns how many
    // moved. Writing the list may throw, for MachinesWindow to report.
    private async Task<int> RefreshAddressesAsync(IReadOnlyList<FoundMachine> found)
    {
        var roster = _tiles.Select(tile => tile.Machine).ToList();
        var proven = await AddressCheck.Proven(WithOld(roster, Roster.NewAddresses(roster, found)), AgentAnswers);
        var moved = Move(proven);
        _addressSearch.Searched(DateTime.UtcNow, moved);
        return moved;
    }

    private IReadOnlyList<FoundMachine> SearchNetwork() =>
        DiscoveryClient.Search(
            _masterCertificate.Thumbprint, WakeOnLanSender.DiscoveryTargets(WakeOnLanSender.LocalNetworks(), Discovery.Port), Discovery.ListenTime);

    private bool AgentAnswers(string address, string agentThumbprint) =>
        AddressCheck.AgentAnswers(address, agentThumbprint, _masterCertificate);

    private static IReadOnlyList<(KnownMachine Old, KnownMachine Updated)> WithOld(
        IReadOnlyList<KnownMachine> roster, IReadOnlyList<(int Index, KnownMachine Updated)> changes) =>
        changes.Select(change => (roster[change.Index], change.Updated)).ToList();

    private int Move(IReadOnlyList<(KnownMachine Old, KnownMachine Updated)> moves)
    {
        var moved = TileMoves.Apply(this, moves);
        if (moved > 0)
        {
            SaveRoster();
        }
        return moved;
    }

    int ITileMoveHost.IndexOf(KnownMachine machine)
    {
        for (var i = 0; i < _tiles.Count; i++)
        {
            if (_tiles[i].Machine.IsSameEntryAs(machine))
            {
                return i;
            }
        }
        return -1;
    }

    bool ITileMoveHost.IsLocked(int index) => _screenLocks.ContainsKey(_tiles[index]);

    void ITileMoveHost.Replace(int index, KnownMachine updated)
    {
        RemoveTileAt(index);
        InsertTile(index, updated);
    }

    void ITileMoveHost.Lock(int index) => LockScreen(_tiles[index]);

    string? ITileMoveHost.LockLeftOut
    {
        get => _screenLockLeftOut;
        set => _screenLockLeftOut = value;
    }
}
