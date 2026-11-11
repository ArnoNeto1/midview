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
}

// What each kind of device accepts. A phone can have its screen watched and
// locked; the plans that teach the Android agent to block programs and sites
// and update itself turn the rest on here. Control stays for computers.
public static class DeviceSupport
{
    public static bool Supports(DeviceKind kind, ToolbarAction action) => kind switch
    {
        DeviceKind.Windows => true,
        DeviceKind.Android => action is ToolbarAction.View or ToolbarAction.ScreenLock,
        _ => false,
    };

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
