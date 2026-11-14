using System.Runtime.ExceptionServices;
using System.Windows;
using Midview.Master;
using Xunit;

// The Mi password copied from the assistant: cleared when the window closes
// or the Midview exits, only while the clipboard still holds it. A fake
// clipboard: the PC's own is never touched.
public class SecretClipboardTests
{
    [Fact]
    public void Closing_clears_the_password_still_on_the_clipboard()
    {
        InFakeClipboard(clipboard =>
        {
            Assert.True(SecretClipboard.Copy("Escola2026"));
            Assert.Equal("Escola2026", clipboard.Text);

            SecretClipboard.ClearNow();

            Assert.Null(clipboard.Text);
        });
    }

    [Fact]
    public void Something_copied_since_is_left_alone()
    {
        InFakeClipboard(clipboard =>
        {
            Assert.True(SecretClipboard.Copy("Escola2026"));
            clipboard.Text = "outra coisa";

            SecretClipboard.ClearNow();

            Assert.Equal("outra coisa", clipboard.Text);
        });
    }

    // Cleared once: a second ClearNow (the app exiting after the window
    // closed) does not clear what the teacher copied in between.
    [Fact]
    public void Cleared_once_only()
    {
        InFakeClipboard(clipboard =>
        {
            Assert.True(SecretClipboard.Copy("Escola2026"));
            SecretClipboard.ClearNow();
            clipboard.Text = "Escola2026";

            SecretClipboard.ClearNow();

            Assert.Equal("Escola2026", clipboard.Text);
        });
    }

    [Fact]
    public void The_app_exiting_clears_it()
    {
        InFakeClipboard(clipboard =>
        {
            Assert.True(SecretClipboard.Copy("Escola2026"));

            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();

            Assert.Null(clipboard.Text);
        });
    }

    private sealed class FakeClipboard
    {
        public string? Text;
    }

    private static void InFakeClipboard(Action<FakeClipboard> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var clipboard = new FakeClipboard();
            var (put, holds, empty) = (SecretClipboard.Put, SecretClipboard.Holds, SecretClipboard.Empty);
            SecretClipboard.Put = data => clipboard.Text = (string)data.GetData(DataFormats.UnicodeText);
            SecretClipboard.Holds = text => clipboard.Text == text;
            SecretClipboard.Empty = () => clipboard.Text = null;
            try
            {
                body(clipboard);
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                SecretClipboard.ClearNow();
                (SecretClipboard.Put, SecretClipboard.Holds, SecretClipboard.Empty) = (put, holds, empty);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
