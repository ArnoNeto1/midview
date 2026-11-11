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

// What each kind of device accepts. A phone supports none of them yet; the
// plans that teach the Android agent to watch the screen, lock it, block
// programs and sites and update itself turn those on here.
public static class DeviceSupport
{
    public static bool Supports(DeviceKind kind, ToolbarAction action) => kind == DeviceKind.Windows;

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
}
