using System.Drawing;
using System.Windows.Forms;

namespace Midview.Session;

// A strip across the top of the student's screen while the teacher controls
// it. It never takes focus or clicks -- the teacher's clicks go through it to
// whatever is underneath -- and never shows in Alt+Tab or the taskbar, so
// "Fechar programas" leaves it alone like Midview's other windows. Like the
// teacher's messages, it runs on a short-lived STA thread of its own.
public sealed class WinFormsControlNotice : IControlNotice
{
    private readonly object _gate = new();
    private bool _wanted;

    // Bumped by every Show and Hide, so a strip whose thread starts after
    // it was already hidden again closes instead of staying up.
    private int _generation;
    private NoticeForm? _form;

    public void Show()
    {
        lock (_gate)
        {
            if (_wanted)
            {
                return;
            }
            _wanted = true;
            var generation = ++_generation;

            var thread = new Thread(() =>
            {
                var form = new NoticeForm();
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
                    form.BeginInvoke(new Action(form.Close));
                };
                Application.Run(form);
            })
            {
                IsBackground = true,
                Name = "Control notice",
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
            _generation++;
            var form = _form;
            _form = null;
            if (form is not null)
            {
                try
                {
                    form.BeginInvoke(new Action(form.Close));
                }
                catch (InvalidOperationException)
                {
                    // Its window is already gone.
                }
            }
        }
    }

    private sealed class NoticeForm : Form
    {
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        public NoticeForm()
        {
            Text = "Midview";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.FromArgb(176, 0, 32);
            Opacity = 0.9; // also makes it a layered window, which clicks pass through

            Controls.Add(new Label
            {
                Text = "Este computador está sendo controlado pelo professor",
                ForeColor = Color.White,
                Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 13f, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(18, 8, 18, 8),
            });

            Load += (_, _) =>
            {
                var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top);
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return parameters;
            }
        }
    }
}
