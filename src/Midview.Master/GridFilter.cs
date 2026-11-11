namespace Midview.Master;

// Which kind of device the grid shows.
public enum DeviceFilter
{
    All = 0,
    Computers = 1,
    Phones = 2,
}

// "Só online" and "Todos / Computadores / Celulares": which tiles the classroom grid shows, and what it says when
// that leaves none. Only the view changes -- the whole-class actions and the
// machine counts still cover every machine.
public static class GridFilter
{
    // A machine not heard from yet shows up as soon as it answers.
    public static bool Shows(MachineTile tile, bool onlineOnly, DeviceFilter devices = DeviceFilter.All) =>
        (!onlineOnly || tile.Presence == TilePresence.Online) && ShowsDevice(tile, devices);

    private static bool ShowsDevice(MachineTile tile, DeviceFilter devices) => devices switch
    {
        DeviceFilter.Computers => !tile.Machine.IsPhone,
        DeviceFilter.Phones => tile.Machine.IsPhone,
        _ => true,
    };

    // "Celulares" with no phone in the class: the empty grid offers the big
    // "Preparar celulares" button, even with no machine at all (it then takes
    // the place of the classroom's own hint). Not while the phone app is not
    // published (phonesReady false).
    public static bool OffersPhoneSetup(IReadOnlyCollection<MachineTile> tiles, DeviceFilter devices, bool phonesReady) =>
        phonesReady && devices == DeviceFilter.Phones && !tiles.Any(tile => tile.Machine.IsPhone);

    // Shown in the grid instead of leaving it blank; null when something
    // shows, or when there are no machines at all (the classroom's own hint),
    // except with the phone setup offered.
    public static string? EmptyNotice(
        IReadOnlyCollection<MachineTile> tiles, bool onlineOnly, DeviceFilter devices = DeviceFilter.All,
        bool phonesReady = true)
    {
        if (OffersPhoneSetup(tiles, devices, phonesReady))
        {
            return "Nenhum celular nesta sala ainda.";
        }
        if ((!onlineOnly && devices == DeviceFilter.All) || tiles.Count == 0 || tiles.Any(tile => Shows(tile, onlineOnly, devices)))
        {
            return null;
        }
        if (!tiles.Any(tile => ShowsDevice(tile, devices)))
        {
            return devices == DeviceFilter.Phones
                ? "Nenhum celular nesta sala ainda."
                : "Nenhum computador nesta sala: só há celulares.";
        }
        return tiles.Any(tile => ShowsDevice(tile, devices) && tile.Presence == TilePresence.Connecting)
            ? "Procurando as máquinas ligadas..."
            : "Nenhuma máquina online agora: todas estão desligadas ou sem resposta.";
    }
}
