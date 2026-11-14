using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Midview.Master;

// Copies a password to the clipboard for a short while: kept out of the
// clipboard history and the cloud clipboard, and cleared after the time, when
// the window that copied it closes (ClearNow) or when the Midview exits,
// unless something else was copied meanwhile.
internal static class SecretClipboard
{
    public static readonly TimeSpan Keep = TimeSpan.FromSeconds(60);

    // The clipboard as the copies see it: the system's, a fake one in the tests.
    internal static Action<DataObject> Put { get; set; } = data => Clipboard.SetDataObject(data, copy: true);
    internal static Func<string, bool> Holds { get; set; } = text => Clipboard.ContainsText() && Clipboard.GetText() == text;
    internal static Action Empty { get; set; } = Clipboard.Clear;

    // The password copied last and not cleared yet, and its timer.
    private static string? _secret;
    private static DispatcherTimer? _timer;
    private static Dispatcher? _watched;

    // False when the clipboard is busy (another program holds it).
    public static bool Copy(string secret)
    {
        var data = new DataObject();
        data.SetText(secret);
        // Windows' formats: not in Win+V's history, not sent to other devices.
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        try
        {
            Put(data);
        }
        catch (Exception ex) when (ex is ExternalException)
        {
            return false;
        }

        _timer?.Stop();
        _secret = secret;
        _timer = new DispatcherTimer { Interval = Keep };
        _timer.Tick += (_, _) => ClearNow();
        _timer.Start();
        if (_watched != Dispatcher.CurrentDispatcher)
        {
            // The copy outlives the program (copy: true): taken back on the way out.
            _watched = Dispatcher.CurrentDispatcher;
            _watched.ShutdownStarted += (_, _) => ClearNow();
        }
        return true;
    }

    // Clears the password now, if the clipboard still holds it; anything
    // copied since is left alone.
    public static void ClearNow()
    {
        _timer?.Stop();
        _timer = null;
        if (_secret is not { } secret)
        {
            return;
        }
        _secret = null;
        try
        {
            if (Holds(secret))
            {
                Empty();
            }
        }
        catch (Exception ex) when (ex is ExternalException)
        {
            // Busy: the password stays until something else is copied.
        }
    }

    // The e-mail is no secret: a plain copy.
    public static bool CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception ex) when (ex is ExternalException)
        {
            return false;
        }
    }
}
