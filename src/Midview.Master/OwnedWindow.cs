using System.Windows;

namespace Midview.Master;

// A window the teacher went back to from another program (its own taskbar
// button, Alt+Tab) brings that program back to the front when it closes, not
// Midview: Windows activates whatever was active before it. So the window it
// belongs to is activated first, while this one still exists and Midview may
// still take the front.
public static class OwnedWindow
{
    public static void ReturnToOwnerWhenClosed(Window window) =>
        window.Closing += (_, e) =>
        {
            if (!e.Cancel && window.IsActive && window.Owner is { IsVisible: true } owner)
            {
                owner.Activate();
            }
        };
}
