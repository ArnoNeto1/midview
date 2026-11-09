namespace Midview.Session;

// Where a Desktop icon is, in the Desktop view's own coordinates.
public readonly record struct IconPoint(int X, int Y);

// The icons on the student's Desktop, as Explorer shows them (their own and the
// shared ones), for DesktopIconGuard. Behind an interface so the tests never
// move the developer's real icons.
public interface IDesktopIcons
{
    // Each icon by its parsing name (a file's full path, "::{GUID}" for This
    // PC, the Recycle Bin...) and where it is; null when Explorer's Desktop
    // can't be reached right now (Explorer starting or restarting).
    IReadOnlyDictionary<string, IconPoint>? Read();

    // "Organizar ícones automaticamente" turned off when it is on: while it is
    // on, Windows places every icon itself and ignores Place. True when it was on.
    bool TurnOffAutoArrange();

    // These icons put at these positions; names no longer there are skipped.
    void Place(IReadOnlyDictionary<string, IconPoint> positions);
}
