namespace Midview.Master;

// One machine's chat, as ClassChat sees it: ChatConnection on the network,
// a fake in the tests.
public interface IChatConnection : IDisposable
{
    // Raised on whichever thread finds out: StudentMessageReceived on a
    // background thread; Closed there too when the student's side ends it,
    // or on the caller's thread from Dispose or a Send that fails. Closed is
    // raised once, however the conversation ended, with a short reason.
    event Action<string>? StudentMessageReceived;
    event Action<string>? Closed;

    void Start();

    // False if the line couldn't be sent; the conversation is over then.
    // Throws EncoderFallbackException for text that isn't valid UTF-16
    // (pasted with half an emoji), leaving the conversation on.
    bool Send(string text);
}
