namespace Midview.Session;

public interface IAppCloser
{
    // Returns how many windows were asked to close and how many programs had
    // to be ended by force (always 0 when not forcing).
    (int Asked, int Forced) CloseAll(bool force);
}
