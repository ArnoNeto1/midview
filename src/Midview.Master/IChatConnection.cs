namespace Midview.Master;

// One machine's chat, as ClassChat sees it: ChatConnection on the network,
// a fake in the tests.
public interface IChatConnection : IDisposable
{
    // Both raised on a background thread. Closed is raised once, however the
    // conversation ended (including Dispose), with a short reason.
    event Action<string>? StudentMessageReceived;
    event Action<string>? Closed;

    void Start();

    // False if the line couldn't be sent; the conversation is over then.
    bool Send(string text);
}
