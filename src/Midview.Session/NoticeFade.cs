using System.Drawing;

namespace Midview.Session;

// How the "being controlled" strip gets out of the teacher's way: while the
// mouse is on it or close to it, it fades to almost nothing, so the teacher
// sees what's under it (clicks always went through it); once the mouse
// leaves, it comes back. The strip's timer takes one Next step per tick.
public static class NoticeFade
{
    // As the strip always was.
    public const double Shown = 0.9;

    // Not zero, so the student still sees a trace of it.
    public const double Faded = 0.1;

    // Per tick: from shown to faded, or back, in four ticks.
    public const double Step = 0.25;

    // How close the mouse must come, in pixels at 100% scaling.
    public const int Reach = 40;

    public static bool IsNear(Rectangle strip, Point cursor, double dpiScale)
    {
        var reach = (int)Math.Round(Reach * dpiScale);
        return Rectangle.Inflate(strip, reach, reach).Contains(cursor);
    }

    public static double Next(double opacity, bool near)
    {
        var target = near ? Faded : Shown;
        return opacity < target ? Math.Min(target, opacity + Step) : Math.Max(target, opacity - Step);
    }
}
