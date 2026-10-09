using System.Runtime.InteropServices;

namespace Midview.Session;

// The student machine's clipboard, as the clipboard sync uses it. Apart from
// the Windows implementation so the sync can be tested with a fake.
public interface IClipboard
{
    // The clipboard's text, or null when it holds no text (or can't be read
    // right now -- another app has it locked).
    string? GetText();

    // Replaces the clipboard's text. Best effort: a clipboard locked by
    // another app just means this doesn't take.
    void SetText(string text);
}

// Reads and writes the Windows clipboard, which must be touched from an STA
// thread -- so each call runs on a short-lived STA thread of its own. The
// calls are rare (at most about one a second while controlling), so a thread
// per call is cheaper than owning one.
public sealed class WindowsClipboard : IClipboard
{
    public string? GetText() => RunSta(() =>
        System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : null);

    public void SetText(string text) => RunSta<object?>(() =>
    {
        if (!string.IsNullOrEmpty(text))
        {
            System.Windows.Forms.Clipboard.SetText(text);
        }
        return null;
    });

    private static T RunSta<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }
        return result;
    }
}
