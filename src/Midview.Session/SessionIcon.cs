using System.Drawing;
using System.Windows.Forms;

namespace Midview.Session;

// The Midview logo for the student's windows that show a title bar: the
// icon built into this program (assets\midview.ico), or Windows' default
// one if it can't be read.
internal static class SessionIcon
{
    public static void ApplyTo(Form form)
    {
        try
        {
            if (Icon.ExtractAssociatedIcon(Application.ExecutablePath) is { } icon)
            {
                form.Icon = icon;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // The default icon is fine.
        }
    }
}
