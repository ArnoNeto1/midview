using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Threading;
using Midview.Protocol;

namespace Midview.Master;

// Machines of the room found at a new address: by "Procurar na rede"
// (MachinesWindow calls RefreshAddresses), and by the Master itself while a
// machine is offline (AddressSearchSchedule). Both use the same search
// (DiscoveryClient) and the same matching by Agent (Roster.NewAddresses):
// only machines already in the list move; none is ever added or paired.
// The Master's own search is quiet: no window, no status line.
public partial class MainWindow
{
    // How often the Master checks whether a search is due.
    private static readonly TimeSpan AddressSearchCheck = TimeSpan.FromSeconds(5);

    private readonly AddressSearchSchedule _addressSearch = new(DateTime.UtcNow);
    private DispatcherTimer? _addressSearchTimer;
    private bool _addressSearching;

    private void StartAddressSearch()
    {
        _addressSearchTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = AddressSearchCheck };
        _addressSearchTimer.Tick += async (_, _) => await SearchAddressesIfDueAsync();
        _addressSearchTimer.Start();
    }

    private void StopAddressSearch()
    {
        _addressSearchTimer?.Stop();
        _addressSearchTimer = null;
    }

    private async Task SearchAddressesIfDueAsync()
    {
        var offline = _tiles
            .Where(tile => tile.Presence == TilePresence.Offline && Roster.MovesBySearch(tile.Machine))
            .Select(tile => tile.Machine.AgentThumbprint!);
        if (!_addressSearch.Due(DateTime.UtcNow, offline) || _addressSearching)
        {
            return;
        }

        _addressSearching = true;
        var moved = 0;
        try
        {
            var thumbprint = _masterCertificate.Thumbprint;
            var found = await Task.Run(() => DiscoveryClient.Search(
                thumbprint, WakeOnLanSender.DiscoveryTargets(WakeOnLanSender.LocalNetworks(), Discovery.Port), Discovery.ListenTime));
            if (_addressSearchTimer is null)
            {
                return; // the Master closed meanwhile
            }

            // The list as it is now: the teacher may have changed it meanwhile.
            var roster = _tiles.Select(tile => tile.Machine).ToList();
            var changes = Roster.NewAddressesOfOffline(roster, found, index => _tiles[index].Presence == TilePresence.Offline);
            moved = changes.Count;
            MoveTiles(changes);
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException or NetworkInformationException)
        {
            // Quiet: tried again later. A list that couldn't be written keeps
            // the new addresses on the grid; the next change saves them.
        }
        finally
        {
            _addressSearching = false;
            _addressSearch.Searched(DateTime.UtcNow, moved);
        }
    }

    // Called by MachinesWindow after a search: machines found at another
    // address (see Roster.NewAddresses). Returns how many moved.
    private int RefreshAddresses(IReadOnlyList<FoundMachine> found)
    {
        var changes = Roster.NewAddresses(_tiles.Select(tile => tile.Machine).ToList(), found);
        _addressSearch.Searched(DateTime.UtcNow, changes.Count);
        MoveTiles(changes);
        return changes.Count;
    }

    // Each moved machine gets a new tile (its poller then asks at the new
    // address). One whose screen was meant to be locked is locked again
    // there: replacing the tile gives the screen back otherwise.
    private void MoveTiles(IReadOnlyList<(int Index, KnownMachine Updated)> changes)
    {
        foreach (var (index, updated) in changes)
        {
            var wasLocked = _screenLocks.ContainsKey(_tiles[index]);
            var leftOut = _screenLockLeftOut;
            RemoveTileAt(index);
            InsertTile(index, updated);
            if (wasLocked)
            {
                _screenLockLeftOut = leftOut;
                LockScreen(_tiles[index]);
            }
        }
        if (changes.Count > 0)
        {
            SaveRoster();
        }
    }
}
