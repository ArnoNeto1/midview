namespace Midview.Master;

// Which kind of device the grid shows.
public enum DeviceFilter
{
    All = 0,
    Computers = 1,
    Phones = 2,
    Tablets = 3,
}

// "Só online" and "Todos / Computadores / Celulares / Tablets": which tiles the classroom grid shows, and what it says when
// that leaves none. "Só online" only changes the view; "Telas" also narrows
// what the toolbar's buttons reach (DeviceSupport.ToolbarReach). The machine
// counts still cover every machine.
public static class GridFilter
{
    // A machine not heard from yet shows up as soon as it answers.
    public static bool Shows(MachineTile tile, bool onlineOnly, DeviceFilter devices = DeviceFilter.All) =>
        (!onlineOnly || tile.Presence == TilePresence.Online) && ShowsDevice(tile, devices);

    private static bool ShowsDevice(MachineTile tile, DeviceFilter devices) => DeviceSupport.Shows(tile.Machine, devices);

    // "Celulares" with no phone in the class (or "Tablets" with no tablet):
    // the empty grid offers the big "Preparar celulares" ("Preparar tablets")
    // button, even with no machine at all (it then takes the place of the
    // classroom's own hint). Only with the assistant out (phonesReady:
    // PhoneProvisioning.WizardReady, PhoneSetupOffer.Wizard).
    public static bool OffersPhoneSetup(IReadOnlyCollection<MachineTile> tiles, DeviceFilter devices, bool phonesReady) =>
        phonesReady && DeviceSupport.AndroidKindOf(devices) is { } kind
        && !tiles.Any(tile => DeviceSupport.IsAndroidOf(tile.Machine, kind));

    // Shown in the grid instead of leaving it blank; null when something
    // shows, or when there are no machines at all (the classroom's own hint),
    // except with the phone setup offered. phoneSetup says what an empty
    // Celulares (or Tablets) tells about getting the first phone (tablet).
    public static string? EmptyNotice(
        IReadOnlyCollection<MachineTile> tiles, bool onlineOnly, DeviceFilter devices = DeviceFilter.All,
        PhoneSetupOffer phoneSetup = PhoneSetupOffer.Soon)
    {
        if (OffersPhoneSetup(tiles, devices, phoneSetup == PhoneSetupOffer.Wizard))
        {
            return PhoneProvisioning.NoDeviceYet(DeviceSupport.AndroidKindOf(devices)!.Value, PhoneSetupOffer.Soon);
        }
        if ((!onlineOnly && devices == DeviceFilter.All) || tiles.Count == 0 || tiles.Any(tile => Shows(tile, onlineOnly, devices)))
        {
            return null;
        }
        if (!tiles.Any(tile => ShowsDevice(tile, devices)))
        {
            return DeviceSupport.AndroidKindOf(devices) is { } kind
                ? PhoneProvisioning.NoDeviceYet(kind, phoneSetup)
                : $"Nenhum computador nesta sala: só há {AndroidWords(tiles)}.";
        }
        return tiles.Any(tile => ShowsDevice(tile, devices) && tile.Presence == TilePresence.Connecting)
            ? "Procurando as máquinas ligadas..."
            : "Nenhuma máquina online agora: todas estão desligadas ou sem resposta.";
    }

    // The Android devices of a room with no computer, by what is in it.
    private static string AndroidWords(IReadOnlyCollection<MachineTile> tiles)
    {
        var phones = tiles.Any(tile => DeviceSupport.IsAndroidOf(tile.Machine, AndroidDeviceKind.Phone));
        var tablets = tiles.Any(tile => DeviceSupport.IsAndroidOf(tile.Machine, AndroidDeviceKind.Tablet));
        return phones && tablets ? "celulares e tablets" : tablets ? "tablets" : "celulares";
    }
}
