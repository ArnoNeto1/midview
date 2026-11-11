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

    // Shown in the grid instead of leaving it blank; null when something
    // shows, or when there are no machines at all (the classroom's own hint).
    public static string? EmptyNotice(
        IReadOnlyCollection<MachineTile> tiles, bool onlineOnly, DeviceFilter devices = DeviceFilter.All)
    {
        if ((!onlineOnly && devices == DeviceFilter.All) || tiles.Count == 0 || tiles.Any(tile => Shows(tile, onlineOnly, devices)))
        {
            return null;
        }
        if (!tiles.Any(tile => ShowsDevice(tile, devices)))
        {
            return devices == DeviceFilter.Phones
                ? "Nenhum celular nesta sala ainda. Use \"Preparar celular\" para colocar o primeiro."
                : "Nenhum computador nesta sala: só há celulares.";
        }
        return tiles.Any(tile => ShowsDevice(tile, devices) && tile.Presence == TilePresence.Connecting)
            ? "Procurando as máquinas ligadas..."
            : "Nenhuma máquina online agora: todas estão desligadas ou sem resposta.";
    }
}
