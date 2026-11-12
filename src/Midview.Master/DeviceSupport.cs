using Midview.Protocol;

namespace Midview.Master;

// Every action the teacher's toolbar (or a tile's menu) offers.
public enum ToolbarAction
{
    View,
    Control,
    ScreenLock,
    Power,
    Wake,
    Schedule,
    Message,
    Chat,
    SendFile,
    CollectFiles,
    CloseApps,
    BlockPrograms,
    BlockSites,
    OpenWebsite,
    OpenProgram,
    Install,
    Uninstall,
    LabConfig,
    ClassFolder,
    Update,
    // A tile's menu only: erase a school phone so it can be prepared again.
    WipePhone,
    // Made for phones only: "Otimizar celulares..." in the "Telas" menu and
    // on a phone's tile menu, and updating the phones' Midview ("Atualizações"
    // in "Máquinas da sala", and the tile menu).
    Optimize,
    UpdatePhones,
}

// What each kind of device accepts. A phone can have its screen watched and
// locked, its apps blocked (its own window: PhoneAppBlockWindow) and its
// sites blocked (the same commands as a PC's), and is optimized and updated
// (actions a PC has nothing to do with). Control stays for computers.
// WipePhone (a tile's menu only) is for Android devices alone.
public static class DeviceSupport
{
    public static bool Supports(DeviceKind kind, ToolbarAction action) => kind switch
    {
        DeviceKind.Windows => !IsPhoneOnly(action) && action != ToolbarAction.WipePhone,
        DeviceKind.Android => action is ToolbarAction.View or ToolbarAction.ScreenLock
            or ToolbarAction.BlockPrograms or ToolbarAction.BlockSites
            or ToolbarAction.WipePhone
            || IsPhoneOnly(action),
        _ => false,
    };

    // The actions made for phones only.
    public static bool IsPhoneOnly(ToolbarAction action) => action is ToolbarAction.Optimize or ToolbarAction.UpdatePhones;

    // "Celulares" in the "Telas" menu, and the "Atualizar os celulares" card
    // of "Máquinas da sala", are for a room with at least one phone.
    public static bool HasPhones(IEnumerable<KnownMachine> room) => room.Any(machine => machine.IsPhone);

    // An Android device: a phone or a tablet, which run the same agent and
    // are optimized alike. A tablet kind of its own, when there is one, goes here too.
    public static bool IsAndroid(KnownMachine machine) => machine.Kind is DeviceKind.Android;

    // "Otimizar celulares e tablets..." in the "Telas" menu: a room with an Android device.
    public static bool HasAndroid(IEnumerable<KnownMachine> room) => room.Any(IsAndroid);

    // The machines that can do the action, in order: what a feature that
    // phones do not take part in (the class folder, the Windows update) is fed.
    public static IReadOnlyList<KnownMachine> Only(IEnumerable<KnownMachine> machines, ToolbarAction action) =>
        machines.Where(machine => Supports(machine.Kind, action)).ToList();

    // True when at least one of the machines can do it.
    public static bool IsEnabled(IReadOnlyCollection<KnownMachine> selected, ToolbarAction action) =>
        selected.Any(machine => Supports(machine.Kind, action));

    // Said before acting on several machines when some can't do it; null
    // when none is left out.
    public static string? LeftOutNotice(IReadOnlyCollection<KnownMachine> targets, ToolbarAction action)
    {
        var leftOut = targets.Count(machine => !Supports(machine.Kind, action));
        return leftOut switch
        {
            0 => null,
            1 => "1 celular ficou de fora: essa ação ainda não funciona em celular.",
            _ => $"{leftOut} celulares ficaram de fora: essa ação ainda não funciona em celular.",
        };
    }

    // Whom a toolbar button reaches: the whole room, as always, except for
    // blocking programs and sites when only phones are selected, or nothing is
    // selected and the grid shows only phones: then those phones ("Bloquear
    // programas" then opens the phones' window); and when the grid shows only
    // computers: then every PC and no phone, which the teacher cannot see
    // there (none when the room has no PC). PCs are reached as before. The
    // phone actions reach the selected phones, or every phone of the room.
    public static IReadOnlyList<KnownMachine> ToolbarReach(
        ToolbarAction action, IReadOnlyList<KnownMachine> selected, IReadOnlyList<KnownMachine> room, DeviceFilter shown)
    {
        // The phone actions: the selected Android devices (phones and
        // tablets), or every one of the room when none is selected.
        if (IsPhoneOnly(action))
        {
            var chosen = selected.Where(IsAndroid).ToList();
            return chosen.Count > 0 ? chosen : room.Where(IsAndroid).ToList();
        }
        if (action is not (ToolbarAction.BlockPrograms or ToolbarAction.BlockSites))
        {
            return room;
        }
        if (selected.Count > 0 && selected.All(machine => machine.IsPhone))
        {
            return selected;
        }
        if (shown == DeviceFilter.Computers)
        {
            return room.Where(machine => !machine.IsPhone).ToList();
        }
        if (selected.Count == 0 && shown == DeviceFilter.Phones && room.Any(machine => machine.IsPhone))
        {
            return room.Where(machine => machine.IsPhone).ToList();
        }
        return room;
    }

    // For an action made for phones only: said after it when PCs were among the targets; null when none.
    public static string? PhoneOnlyLeftOutNotice(IReadOnlyCollection<KnownMachine> targets)
    {
        var leftOut = targets.Count(machine => !machine.IsPhone);
        return leftOut switch
        {
            0 => null,
            1 => "1 computador ficou de fora: essa ação só funciona em celular.",
            _ => $"{leftOut} computadores ficaram de fora: essa ação só funciona em celular.",
        };
    }
}

// The action a toolbar button or a tile's menu item stands for, set in the
// XAML ("Power", "SendFile"...): MainWindow greys the ones that do not work
// on the machines they would reach.
public static class DeviceActions
{
    public static readonly System.Windows.DependencyProperty ActionProperty =
        System.Windows.DependencyProperty.RegisterAttached(
            "Action", typeof(string), typeof(DeviceActions), new System.Windows.PropertyMetadata(null));

    public static string? GetAction(System.Windows.DependencyObject element) =>
        (string?)element.GetValue(ActionProperty);

    public static void SetAction(System.Windows.DependencyObject element, string? value) =>
        element.SetValue(ActionProperty, value);

    public static ToolbarAction? Of(System.Windows.DependencyObject element) =>
        Enum.TryParse<ToolbarAction>(GetAction(element), out var action) ? action : null;
}
