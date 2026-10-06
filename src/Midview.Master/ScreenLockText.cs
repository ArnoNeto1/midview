namespace Midview.Master;

// The line a tile shows while its machine is meant to be locked. A machine
// that was locked and then isn't -- the student signed out, restarted or
// switched user, all of which Ctrl+Alt+Del allows and nothing short of a
// driver can stop -- is flagged until its ScreenLockKeeper locks it again.
public static class ScreenLockText
{
    public const string Locking = "🔒 Bloqueando...";

    public static (string Text, bool Alert) Badge(bool locked, bool wasLocked, string detail) =>
        locked ? ("🔒 Tela bloqueada", false)
        : wasLocked ? ($"⚠ Saiu do bloqueio ({detail})", true)
        : ($"⚠ Não bloqueou ({detail})", true);

    // The toolbar's one screen-lock button: it locks the class, and while
    // any screen is locked (the class's or a single machine's) it gives them
    // all back instead. Badge is a Segoe Fluent Icons glyph: a closed or an
    // open padlock.
    public static (string Label, string Badge, string ToolTip) ClassButton(bool anyLocked) =>
        anyLocked
            ? ("Desbloquear telas", "", "Devolve a tela, o teclado e o mouse a todos os alunos.")
            : ("Bloquear telas", "", "Cobre a tela de todos os alunos com um cadeado e trava o teclado e o mouse.");
}
