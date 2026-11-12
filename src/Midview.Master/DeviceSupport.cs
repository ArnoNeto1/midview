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
    // in "Máquinas da sala", and the tile menu), and the lock screen PIN
    // ("Senha dos celulares e tablets..." in the "Telas" menu).
    Optimize,
    UpdatePhones,
    PhonePin,
}

// What each kind of device accepts. A phone can have its screen watched and
// locked, its apps blocked (its own window: PhoneAppBlockWindow) and closed
// ("Fechar programas": all of them at once, CloseAppsTargets) and its sites
// blocked (the same commands as a PC's), gets the teacher's messages and
// chats ("Mensagem", "Bate-papo": MessageTargets, ChatConnection) and files
// ("Enviar arquivo", "Recolher arquivos": FileTargets, its Download/Midview), and is
// optimized and updated (actions a PC has nothing to do with). Control stays
// for computers. WipePhone (a tile's menu only) is for Android devices alone.
public static class DeviceSupport
{
    public static bool Supports(DeviceKind kind, ToolbarAction action) => kind switch
    {
        DeviceKind.Windows => !IsPhoneOnly(action) && action != ToolbarAction.WipePhone,
        DeviceKind.Android => action is ToolbarAction.View or ToolbarAction.ScreenLock
            or ToolbarAction.BlockPrograms or ToolbarAction.BlockSites
            or ToolbarAction.CloseApps or ToolbarAction.Message or ToolbarAction.Chat
            or ToolbarAction.SendFile or ToolbarAction.CollectFiles
            or ToolbarAction.WipePhone
            || IsPhoneOnly(action),
        _ => false,
    };

    // The actions made for phones only.
    public static bool IsPhoneOnly(ToolbarAction action) =>
        action is ToolbarAction.Optimize or ToolbarAction.UpdatePhones or ToolbarAction.PhonePin;

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

    // Whether the "Telas" filter shows the machine: Todos every one,
    // Computadores the PCs, Celulares the phones and tablets. The grid
    // (GridFilter) and the toolbar's reach (ToolbarReach) go by it.
    public static bool Shows(KnownMachine machine, DeviceFilter shown) => shown switch
    {
        DeviceFilter.Computers => !IsAndroid(machine),
        DeviceFilter.Phones => IsAndroid(machine),
        _ => true,
    };

    // Whom a toolbar button reaches: what the teacher sees. The selection
    // when there is one (it only ever holds tiles the grid shows); otherwise
    // every machine the "Telas" filter shows (Todos: the whole room;
    // Computadores: the PCs; Celulares: the phones and tablets). "Só online"
    // does not narrow it: a machine that is off is still there to be turned
    // on. The phone actions keep the Android devices of it. A button whose
    // action none of them can do is greyed (IsEnabled); the ones among them
    // that cannot do it are left out with a notice (LeftOutNotice).
    public static IReadOnlyList<KnownMachine> ToolbarReach(
        ToolbarAction action, IReadOnlyList<KnownMachine> selected, IReadOnlyList<KnownMachine> room, DeviceFilter shown) =>
        ToolbarReach(action, selected, room, shown, machine => machine);

    // The same rule over the grid's tiles (machineOf gives each one's machine).
    public static IReadOnlyList<T> ToolbarReach<T>(
        ToolbarAction action, IReadOnlyList<T> selected, IReadOnlyList<T> room, DeviceFilter shown, Func<T, KnownMachine> machineOf)
    {
        var seen = selected.Count > 0 ? selected : room.Where(item => Shows(machineOf(item), shown)).ToList();
        return IsPhoneOnly(action) ? seen.Where(item => IsAndroid(machineOf(item))).ToList() : seen;
    }

    // Said in the status bar while nothing is selected and the "Telas" filter
    // leaves machines out: whom the toolbar reaches ("Só nos celulares (3).").
    // Null with a selection (the bar names it), with Todos, when the filter
    // hides nothing, and when it shows nothing (the grid says so).
    public static string? ReachNotice(DeviceFilter shown, int selected, int reached, int room)
    {
        if (selected > 0 || reached == 0 || reached == room)
        {
            return null;
        }
        return shown switch
        {
            DeviceFilter.Phones => $"Só nos celulares ({reached}).",
            DeviceFilter.Computers => $"Só nos computadores ({reached}).",
            _ => null,
        };
    }

    // Said when a toolbar button finds none of the machines it would reach:
    // the "Telas" filter shows no machine of its kind ("Nenhum celular na
    // lista da sala para desligar."). forWhat: what the button does.
    public static string NoneReachedText(DeviceFilter shown, string forWhat) => shown switch
    {
        DeviceFilter.Phones => $"Nenhum celular na lista da sala para {forWhat}.",
        DeviceFilter.Computers => $"Nenhum computador na lista da sala para {forWhat}.",
        _ => $"Nenhuma máquina na lista da sala para {forWhat}.",
    };

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

// What the toolbar's "Bloquear telas" does to the machines it reaches
// (DeviceSupport.ToolbarReach): while every one of them that can be locked
// is locked, it gives them back; otherwise it locks the ones that are not.
// Machines out of its reach (hidden by "Telas", or left out of a selection)
// keep their lock: switching the filter never unlocks anything by itself.
public static class ScreenLockReach
{
    // True when the button gives the screens back ("Desbloquear telas").
    public static bool Releases<T>(IReadOnlyList<T> reach, Func<T, KnownMachine> machineOf, Func<T, bool> isLocked)
    {
        var lockable = reach.Where(item => DeviceSupport.Supports(machineOf(item).Kind, ToolbarAction.ScreenLock)).ToList();
        return lockable.Count > 0 && lockable.All(isLocked);
    }

    // What one press does: the machines it locks, or the ones it gives back.
    public static (IReadOnlyList<T> Lock, IReadOnlyList<T> Unlock) Plan<T>(
        IReadOnlyList<T> reach, Func<T, KnownMachine> machineOf, Func<T, bool> isLocked) =>
        Releases(reach, machineOf, isLocked)
            ? (Array.Empty<T>(), reach.Where(isLocked).ToList())
            : (reach.Where(item => DeviceSupport.Supports(machineOf(item).Kind, ToolbarAction.ScreenLock) && !isLocked(item)).ToList(),
               Array.Empty<T>());
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
