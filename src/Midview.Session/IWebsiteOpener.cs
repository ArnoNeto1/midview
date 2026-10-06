namespace Midview.Session;

// Opens a website in the student's browser ("Abrir site").
public interface IWebsiteOpener
{
    // Throws if Windows couldn't open it (no browser set up).
    void Open(Uri address);
}

// Windows opens an http(s) address with the default browser the way it
// opens a file with its program, and needs the same kind of thread for it.
public sealed class WindowsWebsiteOpener : IWebsiteOpener
{
    private readonly WindowsFileOpener _shell = new();

    public void Open(Uri address) => _shell.Open(address.AbsoluteUri);
}
