using System.Drawing;
using System.Windows.Forms;

namespace Midview.Session;

// Session has no UI thread of its own (it's a console app), so each message
// gets a short-lived STA thread running its own message loop until the
// student closes the window. Several messages can be open at once.
public sealed class WinFormsMessageDisplay : IMessageDisplay
{
    private static readonly TimeSpan LockCheck = TimeSpan.FromMilliseconds(300);

    private readonly ScreenLockState _lockState;

    public WinFormsMessageDisplay(ScreenLockState? lockState = null)
    {
        _lockState = lockState ?? ScreenLockState.Default;
    }

    public void Show(string text)
    {
        var thread = new Thread(() =>
        {
            // Under the lock screen it would only be hidden: it waits, and
            // shows once the teacher unlocks.
            while (_lockState.IsLocked)
            {
                Thread.Sleep(LockCheck);
            }
            Application.Run(new TeacherMessageForm(text, _lockState));
        })
        {
            IsBackground = true,
            Name = "Teacher message",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    // Meant to be noticed: dark red, the message in big bold white letters,
    // and no X: the student closes it with "OK, ENTENDI" (or Enter). It
    // stays over everything and keeps the focus until then, checked every
    // second -- except under the lock screen, which covers it until it's
    // gone. With several open, the newest does that.
    private sealed class TeacherMessageForm : Form
    {
        private const string FontName = "Segoe UI";
        private static readonly Color Background = Color.DarkRed;
        private static readonly Color HeaderInk = Color.FromArgb(255, 205, 205);

        // The messages on screen, oldest first.
        private static readonly List<TeacherMessageForm> Open = new();

        private readonly string _text;
        private readonly Panel _messageArea;
        private readonly ScreenLockState _lockState;
        private readonly System.Windows.Forms.Timer _keepInFront = new() { Interval = 1000 };

        public TeacherMessageForm(string text, ScreenLockState lockState)
        {
            _lockState = lockState;
            // The text box below wants Windows line breaks; the teacher's
            // text may come with either kind.
            _text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");

            // No caption text and no control box: no title bar and no X.
            Text = string.Empty;
            AccessibleName = TeacherMessageLayout.Header;
            ControlBox = false;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            SessionIcon.ApplyTo(this);
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Background;
            ClientSize = new Size(800, 400);
            Padding = new Padding(24, 16, 24, 20);

            var header = new Label
            {
                Text = TeacherMessageLayout.Header,
                Dock = DockStyle.Top,
                Height = 36,
                Font = new Font(FontName, 12f),
                ForeColor = HeaderInk,
                TextAlign = ContentAlignment.MiddleCenter,
            };

            // Closed by hand: a button's DialogResult only closes a form shown
            // with ShowDialog, and this one runs modeless (Application.Run).
            var ok = new Button
            {
                Text = TeacherMessageLayout.OkButton,
                Size = new Size(250, 64),
                Font = new Font(FontName, 14f, FontStyle.Bold),
                BackColor = Color.White,
                ForeColor = Background,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
            };
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += (_, _) => Close();
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 84 };
            bottom.Controls.Add(ok);
            bottom.Layout += (_, _) =>
                ok.Location = new Point((bottom.ClientSize.Width - ok.Width) / 2, bottom.ClientSize.Height - ok.Height);

            _messageArea = new Panel { Dock = DockStyle.Fill };

            Controls.Add(header);
            Controls.Add(bottom);
            Controls.Add(_messageArea);
            _messageArea.BringToFront(); // docked last, so it fills what the header and the button leave
            AcceptButton = ok;
            Shown += (_, _) =>
            {
                lock (Open)
                {
                    Open.Add(this);
                }
                KeepInFront();
                ok.Focus();
                _keepInFront.Tick += (_, _) => KeepInFront();
                _keepInFront.Start();
            };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _keepInFront.Dispose();
            lock (Open)
            {
                Open.Remove(this);
            }
            base.OnFormClosed(e);
        }

        private void KeepInFront()
        {
            if (_lockState.IsLocked)
            {
                return;
            }
            lock (Open)
            {
                if (Open.Count == 0 || Open[^1] != this)
                {
                    return;
                }
            }
            KeepOnTop.ComeToFront(this);
        }

        // The message goes in once the window has its real size on this
        // screen (scaled for its DPI), so the font can be fitted to it.
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var box = _messageArea.ClientSize;
            var fitted = TeacherMessageLayout.FitFontSize(box, points =>
            {
                using var font = new Font(FontName, points, FontStyle.Bold);
                return TextRenderer.MeasureText(_text, font, new Size(box.Width, int.MaxValue), TextFormatFlags.WordBreak);
            });

            Control message = fitted is { } size
                ? new Label
                {
                    Text = _text,
                    Font = new Font(FontName, size, FontStyle.Bold),
                    ForeColor = Color.White,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    UseMnemonic = false, // so "&" shows as typed
                }
                // Too long even at the smallest size: it scrolls.
                : new TextBox
                {
                    Text = _text,
                    ReadOnly = true,
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    BorderStyle = BorderStyle.None,
                    BackColor = Background,
                    ForeColor = Color.White,
                    Font = new Font(FontName, TeacherMessageLayout.MinFontSize, FontStyle.Bold),
                    Dock = DockStyle.Fill,
                    TabStop = false,
                };
            _messageArea.Controls.Add(message);
        }
    }
}
