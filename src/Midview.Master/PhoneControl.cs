using System.Windows.Input;
using Midview.Protocol;

namespace Midview.Master;

// "Controlar" on a phone (the viewer, ViewControlWindow): what the window
// says, and which of the teacher's keys go as keys. A phone types characters
// (InputEventKind.Text, from the window's TextInput, so the teacher's own
// keyboard layout and accents count) and takes only the keys that edit or
// move (Backspace, Delete, Enter, the arrows) and Android's buttons: Esc is
// "Voltar", the Windows and Home keys "Início"; the window's "Voltar",
// "Início" and "Recentes" buttons send the browser back, browser home and
// context-menu keys (the agent's control/KeyMap.kt).
public static class PhoneControl
{
    public const ushort BackKey = 0xA6;    // VK_BROWSER_BACK
    public const ushort HomeKey = 0xAC;    // VK_BROWSER_HOME
    public const ushort RecentsKey = 0x5D; // VK_APPS

    public const string ControllingMode = "Controlando: o aluno vê o aviso no topo da tela.";

    public const string ToolbarToolTip = "Controla o celular selecionado: o aluno vê um aviso no topo da tela.";

    public const string PcMenuItem = "Controlar (trava o teclado e o mouse do aluno)";

    // The tile menu's item: a phone's student keeps the touch screen.
    public static string MenuItem(IReadOnlyCollection<KnownMachine> machines) =>
        machines.Count > 0 && machines.All(machine => machine.IsPhone) ? "Controlar" : PcMenuItem;

    // The phone said no (CommandResult's detail, as "a acessibilidade ... desligada ...").
    public static string Refused(string machineName, string detail) =>
        string.IsNullOrWhiteSpace(detail)
            ? $"{machineName} não deixou controlar."
            : $"{machineName} não deixou controlar: {detail}.";

    // The connection ended right after StartControl: an agent from before control.
    public static string OldAgent(string machineName) =>
        $"O Midview de {machineName} é de uma versão que ainda não deixa controlar. " +
        "Atualize os celulares (Máquinas da sala > Atualizações) e tente de novo.";

    // True for a key that types a character: it is left to TextInput (and not
    // marked handled, or Windows would not make the character). With Ctrl or
    // Alt held (not AltGr, which is both) nothing is typed.
    public static bool Types(Key key, ModifierKeys modifiers)
    {
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var alt = modifiers.HasFlag(ModifierKeys.Alt);
        if (ctrl != alt)
        {
            return false;
        }
        return key is >= Key.A and <= Key.Z
            or >= Key.D0 and <= Key.D9
            or >= Key.NumPad0 and <= Key.NumPad9
            or Key.Space or Key.Multiply or Key.Add or Key.Subtract or Key.Decimal or Key.Divide or Key.Separator
            or >= Key.Oem1 and <= Key.OemBackslash
            or Key.DeadCharProcessed or Key.ImeProcessed;
    }

    // What one TextInput sends: each UTF-16 unit, control characters left
    // out (Enter and Backspace go as keys).
    public static IReadOnlyList<InputEvent> TextEvents(string text) =>
        text.Where(c => c >= ' ' && c != '\u007F')
            .Select(c => new InputEvent(InputEventKind.Text, 0, 0, c))
            .ToList();
}
