using System.Drawing;
using System.Windows.Forms;

namespace Midview.Session;

// The lesson-start notice: a small box in the bottom-right corner of the
// primary screen's working area (above the taskbar), over everything, that
// never takes the focus from what the student is doing -- like the "being
// controlled" strip (WinFormsControlNotice) -- and closes itself after
// Lifetime, or at once from its X. Never in Alt+Tab or the taskbar. Each
// notice runs on a short-lived STA thread of its own.
public sealed class WinFormsCornerNotice : IClassFolderNotice
{
    // How long a notice stays up when nobody closes it.
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(8);

    // Between the notice and the edges of the working area.
    public const int Margin = 16;

    public void Show(string text)
    {
        var thread = new Thread(() => Application.Run(new NoticeForm(text)))
        {
            IsBackground = true,
            Name = "Class folder notice",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    // Where a notice of that size goes in that working area.
    public static Point PlaceIn(Rectangle workingArea, Size size) =>
        new(workingArea.Right - size.Width - Margin, workingArea.Bottom - size.Height - Margin);

    private sealed class NoticeForm : Form
    {
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private readonly System.Windows.Forms.Timer _closing = new();

        public NoticeForm(string text)
        {
            Text = "Midview";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.FromArgb(32, 64, 112);
            Padding = new Padding(14, 10, 10, 12);

            var family = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
            var lines = text.Split(Environment.NewLine, 2);
            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent,
            };
            layout.Controls.Add(new Label
            {
                Text = lines[0],
                ForeColor = Color.White,
                Font = new Font(family, 12f, FontStyle.Bold),
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Margin = new Padding(0, 0, 8, 6),
            }, 0, 0);

            var close = new Label
            {
                Text = "✕",
                ForeColor = Color.White,
                Font = new Font(family, 11f, FontStyle.Bold),
                AutoSize = true,
                Cursor = Cursors.Hand,
                Margin = new Padding(0),
            };
            close.Click += (_, _) => Close();
            layout.Controls.Add(close, 1, 0);

            if (lines.Length > 1)
            {
                var body = new Label
                {
                    Text = lines[1],
                    ForeColor = Color.White,
                    Font = new Font(family, 10f),
                    AutoSize = true,
                    MaximumSize = new Size(360, 0),
                    Margin = new Padding(0),
                };
                layout.Controls.Add(body, 0, 1);
                layout.SetColumnSpan(body, 2);
            }
            Controls.Add(layout);

            _closing.Interval = (int)Lifetime.TotalMilliseconds;
            _closing.Tick += (_, _) => Close();

            Load += (_, _) =>
            {
                var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
                Location = PlaceIn(area, Size);
                _closing.Start();
            };
            FormClosed += (_, _) => _closing.Dispose();
        }

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
    }
}
