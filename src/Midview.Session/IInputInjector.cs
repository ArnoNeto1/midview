using Midview.Protocol;

namespace Midview.Session;

public interface IInputInjector
{
    void Inject(InputEvent inputEvent);
}
