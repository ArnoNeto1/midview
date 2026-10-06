namespace Midview.Session;

public interface IAppCloser
{
    // Returns how many windows were asked to close and how many programs were
    // ended by force. Forcing ends the programs right away, without asking;
    // only the windows of programs that are never ended (File Explorer's,
    // Store apps') are asked instead. Not forcing, Forced is always 0.
    (int Asked, int Forced) CloseAll(bool force);
}
