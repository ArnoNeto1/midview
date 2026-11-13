using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Midview.Master;

// Copies a password to the clipboard for a short while: kept out of the
// clipboard history and the cloud clipboard, and cleared after the time
// unless something else was copied meanwhile.
internal static class SecretClipboard
{
    public static readonly TimeSpan Keep = TimeSpan.FromSeconds(60);

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
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (Exception ex) when (ex is ExternalException)
        {
            return false;
        }

        var timer = new DispatcherTimer { Interval = Keep };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == secret)
                {
                    Clipboard.Clear();
                }
            }
            catch (Exception ex) when (ex is ExternalException)
            {
                // Busy: the password stays until something else is copied.
            }
        };
        timer.Start();
        return true;
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
