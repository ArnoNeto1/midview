using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Midview.Session;

// One window over every monitor, dark grey with a light-grey padlock in the
// middle of each screen, while the teacher has this computer locked. The
// student's keyboard and mouse are locked meanwhile, so it only has to stay
// on top; it still refuses to be closed by anyone but this class, and stays
// out of the taskbar and Alt+Tab. Like the teacher's messages, it runs on a
// short-lived STA thread of its own. Whether it's up is published
// (ScreenLockState): the teacher's messages and the countdown banner wait
// under it and show once it's gone.
public sealed class WinFormsScreenLock : IScreenLockDisplay
{
    private readonly object _gate = new();
    private readonly ScreenLockState _state;
    private bool _wanted;

    // Bumped by every Show and Hide, so a window whose thread starts after
    // it was already hidden again closes instead of staying up.
    private int _generation;
    private LockForm? _form;

    public WinFormsScreenLock(ScreenLockState? state = null)
    {
        _state = state ?? ScreenLockState.Default;
    }

    public void Show()
    {
        lock (_gate)
        {
            if (_wanted)
            {
                return;
            }
            _wanted = true;
            _state.Publish(true);
            var generation = ++_generation;

            var thread = new Thread(() =>
            {
                var form = new LockForm();
                form.Load += (_, _) =>
                {
                    lock (_gate)
                    {
                        if (_generation == generation)
                        {
                            _form = form;
                            return;
                        }
                    }
                    form.BeginInvoke(new Action(form.Unlock));
                };
                Application.Run(form);
            })
            {
                IsBackground = true,
                Name = "Screen lock",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }

    public void Hide()
    {
        lock (_gate)
        {
            if (!_wanted)
            {
                return;
            }
            _wanted = false;
            _state.Publish(false);
            _generation++;
            var form = _form;
            _form = null;
            if (form is not null)
            {
                try
                {
                    form.BeginInvoke(new Action(form.Unlock));
                }
                catch (InvalidOperationException)
                {
                    // Its window is already gone.
                }
            }
        }
    }

    private sealed class LockForm : Form
    {
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private static readonly Color Background = Color.FromArgb(64, 64, 64);
        private static readonly Color Padlock = Color.FromArgb(220, 220, 220);

        // Checked every second: anything that comes up over the lock (a
        // program's own topmost window, the taskbar) goes back behind it.
        private readonly System.Windows.Forms.Timer _keepInFront = new() { Interval = 1000 };

        private bool _unlocking;

        public LockForm()
        {
            Text = "Midview";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Background;
            DoubleBuffered = true;
            Cursor = Cursors.Default;
        }

        public void Unlock()
        {
            _unlocking = true;
            Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            KeepOnTop.ComeToFront(this);
            _keepInFront.Tick += (_, _) => KeepOnTop.ComeToFront(this);
            _keepInFront.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _keepInFront.Dispose();
            base.OnFormClosed(e);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TOOLWINDOW;
                return parameters;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 and the like; Windows shutting down still goes through.
            if (!_unlocking && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var screen in Screen.AllScreens)
            {
                var bounds = screen.Bounds;
                bounds.Offset(-Left, -Top); // the window starts at the virtual screen's corner
                DrawPadlock(e.Graphics, bounds);
            }
        }

        // A flat padlock, 5 wide by 6 tall, a bit over a quarter of the
        // screen's height: the lower half a solid body, the upper half a
        // shackle that is a thick arch. Drawn, not an image, so it stays
        // sharp at any resolution.
        private static void DrawPadlock(Graphics graphics, Rectangle screen)
        {
            var height = screen.Height * 0.28f;
            var unit = height / 6f;
            var width = unit * 5f;
            var left = screen.Left + (screen.Width - width) / 2f;
            var top = screen.Top + (screen.Height - height) / 2f;

            using var brush = new SolidBrush(Padlock);
            graphics.FillRectangle(brush, left, top + unit * 3f, width, unit * 3f);

            // The arch's centre line: legs 1.25 units in from each side,
            // half a unit thick, rising from the body to a round top.
            using var pen = new Pen(Padlock, unit / 2f);
            var legLeft = left + unit * 1.25f;
            var legRight = left + width - unit * 1.25f;
            var radius = (legRight - legLeft) / 2f;
            var archTop = top + unit / 4f;
            using var arch = new GraphicsPath();
            arch.AddLine(legLeft, top + unit * 3f, legLeft, archTop + radius);
            arch.AddArc(legLeft, archTop, radius * 2f, radius * 2f, 180f, 180f);
            arch.AddLine(legRight, archTop + radius, legRight, top + unit * 3f);
            graphics.DrawPath(pen, arch);
        }
    }
}
