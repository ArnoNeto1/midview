namespace Midview.Master;

// Which kind of device the grid shows.
public enum DeviceFilter
{
    All = 0,
    Computers = 1,
    Phones = 2,
}

// "Só online" and "Todos / Computadores / Celulares": which tiles the classroom grid shows, and what it says when
// that leaves none. "Só online" only changes the view; "Telas" also narrows
// what the toolbar's buttons reach (DeviceSupport.ToolbarReach). The machine
// counts still cover every machine.
public static class GridFilter
{
    // A machine not heard from yet shows up as soon as it answers.
    public static bool Shows(MachineTile tile, bool onlineOnly, DeviceFilter devices = DeviceFilter.All) =>
        (!onlineOnly || tile.Presence == TilePresence.Online) && ShowsDevice(tile, devices);

    private static bool ShowsDevice(MachineTile tile, DeviceFilter devices) => DeviceSupport.Shows(tile.Machine, devices);

    // "Celulares" with no phone in the class: the empty grid offers the big
    // "Preparar celulares" button, even with no machine at all (it then takes
    // the place of the classroom's own hint). Only with the assistant out
    // (phonesReady: PhoneProvisioning.WizardReady, PhoneSetupOffer.Wizard).
    public static bool OffersPhoneSetup(IReadOnlyCollection<MachineTile> tiles, DeviceFilter devices, bool phonesReady) =>
        phonesReady && devices == DeviceFilter.Phones && !tiles.Any(tile => tile.Machine.IsPhone);

    // Shown in the grid instead of leaving it blank; null when something
    // shows, or when there are no machines at all (the classroom's own hint),
    // except with the phone setup offered. phoneSetup says what an empty
    // Celulares tells about getting the first phone.
    public static string? EmptyNotice(
        IReadOnlyCollection<MachineTile> tiles, bool onlineOnly, DeviceFilter devices = DeviceFilter.All,
        PhoneSetupOffer phoneSetup = PhoneSetupOffer.Soon)
    {
        if (OffersPhoneSetup(tiles, devices, phoneSetup == PhoneSetupOffer.Wizard))
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
                ? PhoneProvisioning.NoPhoneYet(phoneSetup)
                : "Nenhum computador nesta sala: só há celulares.";
        }
        return tiles.Any(tile => ShowsDevice(tile, devices) && tile.Presence == TilePresence.Connecting)
            ? "Procurando as máquinas ligadas..."
            : "Nenhuma máquina online agora: todas estão desligadas ou sem resposta.";
    }
}
