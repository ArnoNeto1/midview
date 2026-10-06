using System.Drawing;
using System.Windows.Forms;

namespace Midview.Session;

// A red band at the top of the student's screen, counting down to what's
// coming (the teacher closing their programs, a shutdown or a restart):
// over everything, but never taking the keyboard, so the student can save
// their work meanwhile. While the screen is locked it stays hidden -- the
// time keeps running -- and shows once it's unlocked. When the time is up
// it says what happened, then goes away after `linger`.
public sealed class CountdownBanner : Form
{
    private const string FontName = "Segoe UI";
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private static readonly Color Background = Color.DarkRed;

    private readonly string _message;
    private readonly string _doneMessage;
    private readonly DateTime _endsAtUtc;
    private readonly TimeSpan _linger;
    private readonly ScreenLockState _lockState;
    private readonly Label _clock;
    private readonly Label _text;
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 250 };

    public CountdownBanner(string message, string doneMessage, DateTime endsAtUtc, TimeSpan linger, ScreenLockState lockState)
    {
        _message = message;
        _doneMessage = doneMessage;
        _endsAtUtc = endsAtUtc;
        _linger = linger;
        _lockState = lockState;

        Text = "Midview";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Background;
        ClientSize = new Size(880, 104);
        Padding = new Padding(18, 10, 22, 10);

        _clock = new Label
        {
            Dock = DockStyle.Left,
            Width = 150,
            Font = new Font(FontName, 30f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _text = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font(FontName, 13f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,
        };
        Controls.Add(_clock);
        Controls.Add(_text);
        _text.BringToFront(); // docked last, so it takes what the clock leaves

        // Ticking from the start, shown or not: it may begin hidden under
        // the lock screen.
        CreateHandle();
        _tick.Tick += (_, _) => Tick(DateTime.UtcNow);
        _tick.Start();
        ShowTime(DateTime.UtcNow);
    }

    // Started by the Agent in the student's session: the banner alone, until
    // it goes away by itself or the Agent ends it.
    public static void Run(string message, string doneMessage, TimeSpan duration, TimeSpan linger) =>
        Application.Run(new CountdownBanner(message, doneMessage, DateTime.UtcNow + duration, linger, ScreenLockState.Default));

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return parameters;
        }
    }

    // Never over the lock screen.
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(value && !_lockState.IsLocked);

    // Centered at the top of the main screen, as wide as fits.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
        Width = Math.Min(Width, area.Width - 32);
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + 12);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tick.Dispose();
        base.OnFormClosed(e);
    }

    private TimeSpan ShowTime(DateTime nowUtc)
    {
        var left = _endsAtUtc - nowUtc;
        _clock.Text = CountdownText.Clock(left);
        _text.Text = left > TimeSpan.Zero ? _message : _doneMessage;
        return left;
    }

    private void Tick(DateTime nowUtc)
    {
        var left = ShowTime(nowUtc);
        if (-left >= _linger)
        {
            Close();
            return;
        }

        if (_lockState.IsLocked)
        {
            if (Visible)
            {
                Hide();
            }
            return;
        }
        if (!Visible)
        {
            Show();
        }
        if (IsHandleCreated && Visible)
        {
            KeepOnTop.Raise(this);
        }
    }
}

// The countdown banner inside the Session (closing the student's
// programs), on a short-lived STA thread of its own like the messages.
public sealed class WinFormsCountdownNotice : ICountdownNotice
{
    // How long "O professor fechou seus programas." stays up.
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(8);

    public void Show(string message, string doneMessage, TimeSpan duration)
    {
        var endsAtUtc = DateTime.UtcNow + duration;
        var thread = new Thread(() =>
            Application.Run(new CountdownBanner(message, doneMessage, endsAtUtc, Linger, ScreenLockState.Default)))
        {
            IsBackground = true,
            Name = "Countdown banner",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
