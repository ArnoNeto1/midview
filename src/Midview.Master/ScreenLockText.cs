namespace Midview.Master;

// The line a tile shows while its machine is meant to be locked. A machine
// that was locked and then isn't -- the student signed out, restarted or
// switched user, all of which Ctrl+Alt+Del allows and nothing short of a
// driver can stop -- is flagged until its ScreenLockKeeper locks it again.
public static class ScreenLockText
{
    public const string Locking = "🔒 Bloqueando...";

    // The machine answered that its screen is locked (the classroom tile's
    // red dot goes by this one, not by Locking).
    public const string Locked = "🔒 Tela bloqueada";

    // The pill over a phone's narrow tile: the padlock alone ("Bloqueada"
    // was cut to "Bloque…"); the badge's words go in its tooltip.
    public const string PhonePill = "🔒";

    public static (string Text, bool Alert) Badge(bool locked, bool wasLocked, string detail) =>
        locked ? (Locked, false)
        : wasLocked ? ($"⚠ Saiu do bloqueio ({detail})", true)
        : ($"⚠ Não bloqueou ({detail})", true);

    // A machine's menu has one item too, by the same rule for the machines it
    // acts on (the one clicked, or every selected one): it gives them back
    // while any of them is locked, and locks them otherwise.
    public static string MachineMenuItem(bool anyLocked) => anyLocked ? "Desbloquear a tela" : "Bloquear a tela";

    // Segoe Fluent Icons padlocks, drawn inside the button's screen.
    private const string ClosedPadlock = "";
    private const string OpenPadlock = "";

    // The toolbar's one screen-lock button: it locks the class, and while
    // any screen is locked (the class's or a single machine's) it gives them
    // all back instead. The padlock shows how the screens are now: open while
    // they're free, closed while any is locked. The label keeps one word per
    // line in both, so the button doesn't change shape when it flips
    // ("Bloquear telas" alone would fit on one line, "Desbloquear telas" not).
    public static (string Label, string Badge, string ToolTip) ClassButton(bool anyLocked) =>
        anyLocked
            ? ("Desbloquear\ntelas", ClosedPadlock, "Devolve a tela, o teclado e o mouse a todos os alunos.")
            : ("Bloquear\ntelas", OpenPadlock, "Cobre a tela de todos os alunos com um cadeado e trava o teclado e o mouse.");
}
