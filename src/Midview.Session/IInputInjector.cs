using Midview.Protocol;

namespace Midview.Session;

public interface IInputInjector
{
    void Inject(InputEvent inputEvent);

    // Types text as Unicode keystrokes (layout-independent), optionally ending
    // with Enter. Used to fill the UAC admin password.
    void TypeText(string text, bool pressEnter);
}
